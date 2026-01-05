using System.Text.Json;
using StackExchange.Redis;
using Microsoft.Extensions.Logging;

namespace Combats.Services.Battle.State;

public class RedisBattleStateStore : IBattleStateStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisBattleStateStore> _logger;
    private const string StateKeyPrefix = "battle:state:";
    private const string ActionKeyPrefix = "battle:action:";
    private const string ActiveBattlesSetKey = "battle:active";

    // Phase enum values for Lua scripts (must match BattlePhase enum):
    // ArenaOpen = 0, TurnOpen = 1, Resolving = 2, Ended = 3

    public RedisBattleStateStore(
        IConnectionMultiplexer redis,
        ILogger<RedisBattleStateStore> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    private string GetStateKey(Guid battleId) => $"{StateKeyPrefix}{battleId}";
    private string GetActionKey(Guid battleId, int turnIndex, Guid playerId) => 
        $"{ActionKeyPrefix}{battleId}:turn:{turnIndex}:player:{playerId}";

    public async Task<bool> TryInitializeBattleAsync(Guid battleId, BattleState initialState, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = GetStateKey(battleId);

        // Use SETNX for idempotent initialization
        var json = JsonSerializer.Serialize(initialState);
        var setResult = await db.StringSetAsync(key, json, when: When.NotExists);

        if (setResult)
        {
            // Add to active battles set
            await db.SetAddAsync(ActiveBattlesSetKey, battleId.ToString());
            _logger.LogInformation(
                "Initialized battle state for BattleId: {BattleId}, Phase: {Phase}",
                battleId, initialState.Phase);
        }
        else
        {
            _logger.LogInformation(
                "Battle {BattleId} already initialized, skipping (idempotent)",
                battleId);
        }

        return setResult;
    }

    public async Task<BattleState?> GetStateAsync(Guid battleId, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = GetStateKey(battleId);
        RedisValue json = await db.StringGetAsync(key);

        if (!json.HasValue) return null;

        try
        {
            var state = JsonSerializer.Deserialize<BattleState>(json.ToString());
            if (state == null)
            {
                _logger.LogError(
                    "Deserialized battle state is null for BattleId: {BattleId}. This indicates a serialization mismatch.",
                    battleId);
                throw new InvalidOperationException($"Deserialized battle state is null for BattleId: {battleId}");
            }
            return state;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, 
                "Failed to deserialize battle state for BattleId: {BattleId}. JSON may be corrupted or schema changed.",
                battleId);
            throw new InvalidOperationException($"Failed to deserialize battle state for BattleId: {battleId}", ex);
        }
    }

    public async Task<bool> TryOpenTurnAsync(Guid battleId, int turnIndex, DateTime deadlineUtc, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = GetStateKey(battleId);

        var deadlineTicks = deadlineUtc.ToUniversalTime().Ticks;
        // Phase enum: ArenaOpen=0, TurnOpen=1, Resolving=2, Ended=3
        const string script = @"
            local stateJson = redis.call('GET', KEYS[1])
            if not stateJson then
                return 0
            end
            local state = cjson.decode(stateJson)
            -- Cannot open if already ended (Ended=3)
            if state.Phase == 3 then
                return 0
            end
            -- Must open turn N only if LastResolvedTurnIndex == N-1
            local expectedLastResolved = tonumber(ARGV[1]) - 1
            if state.LastResolvedTurnIndex ~= expectedLastResolved then
                return 0
            end
            -- Must be in ArenaOpen (0) or Resolving (2) phase
            if state.Phase ~= 0 and state.Phase ~= 2 then
                return 0
            end
            -- Set to TurnOpen (1)
            state.Phase = 1
            state.TurnIndex = tonumber(ARGV[1])
            state.DeadlineUtcTicks = ARGV[2]
            state.Version = state.Version + 1
            redis.call('SET', KEYS[1], cjson.encode(state))
            return 1
        ";

        var result = await db.ScriptEvaluateAsync(
            script,
            [key],
            [turnIndex, deadlineTicks]);

        var success = (int)result == 1;
        if (success)
        {
            _logger.LogInformation(
                "Opened turn {TurnIndex} for BattleId: {BattleId}",
                turnIndex, battleId);
        }

        return success;
    }

    public async Task<bool> TryMarkTurnResolvingAsync(Guid battleId, int turnIndex, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = GetStateKey(battleId);

        // Phase enum: ArenaOpen=0, TurnOpen=1, Resolving=2, Ended=3
        const string script = @"
            local stateJson = redis.call('GET', KEYS[1])
            if not stateJson then
                return 0
            end
            local state = cjson.decode(stateJson)
            -- Must be in TurnOpen (1) phase and turnIndex must match, and not ended
            if state.Phase ~= 1 or state.TurnIndex ~= tonumber(ARGV[1]) then
                return 0
            end
            -- Set to Resolving (2)
            state.Phase = 2
            state.Version = state.Version + 1
            redis.call('SET', KEYS[1], cjson.encode(state))
            return 1
        ";

        var result = await db.ScriptEvaluateAsync(
            script,
            [key],
            [turnIndex]);

        var success = (int)result == 1;
        if (success)
        {
            _logger.LogInformation(
                "Marked turn {TurnIndex} as Resolving for BattleId: {BattleId}",
                turnIndex, battleId);
        }

        return success;
    }

    public async Task<bool> MarkTurnResolvedAndOpenNextAsync(
        Guid battleId,
        int currentTurnIndex,
        int nextTurnIndex,
        DateTime nextDeadlineUtc,
        int noActionStreak,
        CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = GetStateKey(battleId);

        var deadlineTicks = nextDeadlineUtc.ToUniversalTime().Ticks;
        // Phase enum: ArenaOpen=0, TurnOpen=1, Resolving=2, Ended=3
        const string script = @"
            local stateJson = redis.call('GET', KEYS[1])
            if not stateJson then
                return 0
            end
            local state = cjson.decode(stateJson)
            -- Must be in Resolving (2) phase and current turnIndex must match
            if state.Phase ~= 2 or state.TurnIndex ~= tonumber(ARGV[1]) then
                return 0
            end
            -- Set LastResolvedTurnIndex to resolved turnIndex
            state.LastResolvedTurnIndex = tonumber(ARGV[1])
            -- Set to TurnOpen (1) for next turn
            state.Phase = 1
            state.TurnIndex = tonumber(ARGV[2])
            state.DeadlineUtcTicks = ARGV[3]
            state.NoActionStreakBoth = tonumber(ARGV[4])
            -- Clear NextResolveScheduledUtcTicks (will be set when scheduling)
            state.NextResolveScheduledUtcTicks = 0
            state.Version = state.Version + 1
            redis.call('SET', KEYS[1], cjson.encode(state))
            return 1
        ";

        var result = await db.ScriptEvaluateAsync(
            script,
            [key],
            [currentTurnIndex, nextTurnIndex, deadlineTicks, noActionStreak]);

        var success = (int)result == 1;
        if (success)
        {
            _logger.LogInformation(
                "Resolved turn {CurrentTurnIndex} and opened turn {NextTurnIndex} for BattleId: {BattleId}",
                currentTurnIndex, nextTurnIndex, battleId);
        }

        return success;
    }

    public async Task<bool> EndBattleAndMarkResolvedAsync(Guid battleId, int turnIndex, int noActionStreak, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = GetStateKey(battleId);

        // Phase enum: ArenaOpen=0, TurnOpen=1, Resolving=2, Ended=3
        // Atomic: set Phase=Ended AND LastResolvedTurnIndex=currentTurnIndex AND NoActionStreakBoth
        // This ensures idempotency: duplicates won't republish BattleEnded
        const string script = @"
            local stateJson = redis.call('GET', KEYS[1])
            if not stateJson then
                return 0
            end
            local state = cjson.decode(stateJson)
            -- If already ended (Ended=3), return success (idempotent)
            if state.Phase == 3 then
                return 1
            end
            -- Only end if currently resolving (Resolving=2) the specified turn
            if state.Phase ~= 2 or state.TurnIndex ~= tonumber(ARGV[1]) then
                return 0
            end
            -- Set to Ended (3)
            state.Phase = 3
            state.LastResolvedTurnIndex = tonumber(ARGV[1])
            state.NoActionStreakBoth = tonumber(ARGV[2])
            state.NextResolveScheduledUtcTicks = 0
            state.Version = state.Version + 1
            redis.call('SET', KEYS[1], cjson.encode(state))
            -- Remove from active battles set
            redis.call('SREM', KEYS[2], ARGV[3])
            return 1
        ";

        var result = await db.ScriptEvaluateAsync(
            script,
            new RedisKey[] { key, ActiveBattlesSetKey },
            new RedisValue[] { turnIndex, noActionStreak, battleId.ToString() });

        var success = (int)result == 1;
        if (success)
        {
            _logger.LogInformation(
                "Ended battle and marked turn {TurnIndex} resolved for BattleId: {BattleId}",
                turnIndex, battleId);
        }

        return success;
    }

    public async Task<bool> MarkResolveScheduledAsync(Guid battleId, DateTime scheduledUtc, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = GetStateKey(battleId);

        var scheduledTicks = scheduledUtc.ToUniversalTime().Ticks;
        // Atomically update NextResolveScheduledUtcTicks
        const string script = @"
            local stateJson = redis.call('GET', KEYS[1])
            if not stateJson then
                return 0
            end
            local state = cjson.decode(stateJson)
            -- Only update if battle not ended
            if state.Phase == 3 then
                return 0
            end
            state.NextResolveScheduledUtcTicks = ARGV[1]
            state.Version = state.Version + 1
            redis.call('SET', KEYS[1], cjson.encode(state))
            return 1
        ";

        var result = await db.ScriptEvaluateAsync(
            script,
            new RedisKey[] { key },
            new RedisValue[] { scheduledTicks });

        var success = (int)result == 1;
        if (success)
        {
            _logger.LogInformation(
                "Marked ResolveTurn scheduled for BattleId: {BattleId} at {ScheduledUtc}",
                battleId, scheduledUtc);
        }

        return success;
    }

    public async Task<List<Guid>> GetActiveBattlesAsync(CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var members = await db.SetMembersAsync(ActiveBattlesSetKey);
        
        var battleIds = new List<Guid>();
        foreach (var member in members)
        {
            if (Guid.TryParse(member.ToString(), out var battleId))
            {
                battleIds.Add(battleId);
            }
        }

        return battleIds;
    }

    public async Task StoreActionAsync(Guid battleId, int turnIndex, Guid playerId, string actionPayload, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = GetActionKey(battleId, turnIndex, playerId);

        // Store action with expiration (cleanup after battle ends)
        await db.StringSetAsync(key, actionPayload, TimeSpan.FromHours(1));

        _logger.LogInformation(
            "Stored action for BattleId: {BattleId}, TurnIndex: {TurnIndex}, PlayerId: {PlayerId}",
            battleId, turnIndex, playerId);
    }

    public async Task<(string? PlayerAAction, string? PlayerBAction)> GetActionsAsync(
        Guid battleId,
        int turnIndex,
        Guid playerAId,
        Guid playerBId,
        CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var keyA = GetActionKey(battleId, turnIndex, playerAId);
        var keyB = GetActionKey(battleId, turnIndex, playerBId);

        var actionA = await db.StringGetAsync(keyA);
        var actionB = await db.StringGetAsync(keyB);

        return (
            actionA.HasValue ? actionA.ToString() : null,
            actionB.HasValue ? actionB.ToString() : null
        );
    }

    public async Task<bool> UpdatePlayerHpAsync(Guid battleId, int playerAHp, int playerBHp, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = GetStateKey(battleId);

        // Atomically update HP values
        const string script = @"
            local stateJson = redis.call('GET', KEYS[1])
            if not stateJson then
                return 0
            end
            local state = cjson.decode(stateJson)
            -- Only update if battle not ended
            if state.Phase == 3 then
                return 0
            end
            state.PlayerAHp = tonumber(ARGV[1])
            state.PlayerBHp = tonumber(ARGV[2])
            state.Version = state.Version + 1
            redis.call('SET', KEYS[1], cjson.encode(state))
            return 1
        ";

        var result = await db.ScriptEvaluateAsync(
            script,
            new RedisKey[] { key },
            new RedisValue[] { playerAHp, playerBHp });

        var success = (int)result == 1;
        if (success)
        {
            _logger.LogInformation(
                "Updated HP for BattleId: {BattleId}, PlayerA: {PlayerAHp}, PlayerB: {PlayerBHp}",
                battleId, playerAHp, playerBHp);
        }

        return success;
    }
}

/*
PROMPT FOR CURSOR — REFACTOR TURN LOOP (VARIANT B)
Контекст проекта

Ты работаешь в .NET (ASP.NET) микросервисе Battle / Game Engine для онлайн PvP 1x1 игры (в духе combats.ru).

Текущая архитектура:

Authoritative battle state — Redis.

Межсервисное взаимодействие — RabbitMQ + MassTransit.

Используется transactional outbox + inbox (EF + Postgres).

Ввод игрока — через SignalR.

Бой пошаговый, но delivery realtime.

TURN_SECONDS = 10.

Игрок, не приславший валидное действие до дедлайна, получает NoAction.

Сервер полностью авторитарный.

Redis уже содержит Lua/CAS-методы для:

TryOpenTurn

TryMarkTurnResolving

MarkTurnResolvedAndOpenNext

EndBattleAndMarkResolved

Текущая проблема

Сейчас каждый ход (ResolveTurn) реализован через:

MassTransit message + scheduler,

consumer ResolveTurn,

watchdog, который рескейджулит пропущенные тики.

Это приводит к тому, что каждый ход создаёт записи в:

InboxState

OutboxMessage

OutboxState

При большом количестве боёв это создаёт write-amplification в Postgres и является архитектурно неверным для high-frequency turn loop, где authoritative state уже в Redis.

Цель рефактора

Полностью убрать turn loop из RabbitMQ / MassTransit, и реализовать его как внутренний deadline-driven workflow Battle-сервиса, используя Redis как:

state store,

источник дедлайнов (через ZSET).

MQ + outbox/inbox должны остаться только для:

CreateBattle / EndBattle команд,

BattleCreated / BattleEnded событий,

projection consumers (если есть).

Архитектурное решение (ОБЯЗАТЕЛЬНО СОБЛЮДАТЬ)
1. УБРАТЬ ИЗ TURN LOOP

Полностью удалить использование IMessageScheduler для ResolveTurn.

Удалить очередь ResolveTurn.

Удалить ResolveTurnConsumer.

Удалить BattleWatchdogService целиком.

Turn loop НЕ ДОЛЖЕН использовать MassTransit, RabbitMQ или Postgres.

2. ДОБАВИТЬ REDIS DEADLINE INDEX

Добавить Redis Sorted Set:

Key: battle:deadlines

Member: {battleId}

Score: deadlineUtcTicks (long)

Этот ZSET — единственный источник правды, какие бои требуют резолва.

3. ИЗМЕНИТЬ OPEN TURN ЛОГИКУ

При открытии хода:

Установить в battle state:

Phase = TurnOpen

DeadlineUtcTicks = now + TURN_SECONDS

Добавить/обновить запись в Redis ZSET:

ZADD battle:deadlines deadlineTicks battleId


Использовать существующий RedisBattleStateStore и Lua/CAS-инварианты.

4. ДОБАВИТЬ BACKGROUND WORKER

Добавить TurnDeadlineWorker : BackgroundService в Battle-сервисе.

Поведение воркера:

Работает постоянно.

Периодически (например, каждые 200–500 мс):

Берёт due battles:

ZRANGEBYSCORE battle:deadlines -inf now LIMIT 0 N


Для каждого battleId:

Пытается атомарно перевести бой:

TurnOpen → Resolving


используя TryMarkTurnResolvingAsync.

Если не получилось — пропустить (кто-то другой обработал).

Загружает действия игроков текущего хода.

Применяет правила:

отсутствует/невалидно → NoAction

ведёт NoAction streak

Если бой продолжается:

вызвать MarkTurnResolvedAndOpenNextAsync

вычислить новый дедлайн

ZADD battle:deadlines newDeadline battleId

Если бой завершён:

вызвать EndBattleAndMarkResolvedAsync

ZREM battle:deadlines battleId

один раз опубликовать BattleEnded через MassTransit (outbox допустим).

4.5 EARLY TURN RESOLUTION (ОБЯЗАТЕЛЬНО)

Turn resolution НЕ ДОЛЖЕН всегда ждать истечения дедлайна.

Если оба игрока прислали валидные действия до дедлайна:

SignalR SubmitAction handler обязан попытаться немедленно резолвить ход.

Для раннего резолва:

используется тот же Redis CAS-метод TryMarkTurnResolving.

если CAS успешен:

немедленно выполняется полный turn resolve,

battleId удаляется из battle:deadlines,

открывается следующий ход и ставится новый дедлайн.

если CAS неуспешен:

handler делает no-op (ход уже резолвится кем-то другим).

TurnDeadlineWorker рассматривается исключительно как fallback для случаев:

истёк дедлайн,

игрок не прислал действие,

игрок отключился,

сервис перезапускался.

5. ИНВАРИАНТЫ (ОБЯЗАТЕЛЬНЫ)

Один и только один резолв на ход (обеспечивается Redis CAS).

Повторный резолв одного и того же turnIndex безопасен (no-op).

Потеря/дублирование воркера не приводит к двойному урону.

Turn loop НЕ ПИШЕТ в Postgres.

Inbox/Outbox НЕ АКТИВИРУЮТСЯ для тиков.

ЧТО ОСТАВИТЬ БЕЗ ИЗМЕНЕНИЙ

CreateBattleConsumer

EndBattleConsumer

BattleCreated / BattleEnded события

RedisBattleStateStore и Lua-методы (использовать повторно)

SignalR API для ввода действий игроков

Transactional outbox/inbox для межсервисных команд и событий

КРИТЕРИИ ГОТОВНОСТИ (DEFINITION OF DONE)

ResolveTurn не существует как сообщение/consumer/очередь.

BattleWatchdogService удалён.

Turn loop работает без RabbitMQ.

При 1000 активных боёв:

InboxState / Outbox* не растут от тиков.

Redis содержит ZSET battle:deadlines с ожидаемым размером.

Бой корректно:

резолвит ходы по дедлайну,

применяет NoAction,

завершает бой по DoubleForfeit,

допускает реконнект игрока.

BattleEnded публикуется ровно один раз.

ДОПОЛНИТЕЛЬНО (ЖЕЛАТЕЛЬНО)

Добавить метрики:

размер battle:deadlines

lateness = now - deadline

turns resolved / sec

Логирование только на transitions, не на каждый тик.

Важно:
Не упрощай архитектуру.
Не добавляй синхронные HTTP-вызовы между сервисами.
Не перемещай battle state в Postgres.
Не возвращай scheduler/consumer для turn loop.
*/