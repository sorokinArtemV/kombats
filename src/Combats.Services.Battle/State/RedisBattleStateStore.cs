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
    private string GetActionKey(Guid battleId, int turnIndex, Guid playerId) => $"{ActionKeyPrefix}{battleId}:turn:{turnIndex}:player:{playerId}";

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

        if (!json.HasValue)
            return null;

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
            new RedisKey[] { key },
            new RedisValue[] { turnIndex, deadlineTicks });

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
            new RedisKey[] { key },
            new RedisValue[] { turnIndex });

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
            new RedisKey[] { key },
            new RedisValue[] { currentTurnIndex, nextTurnIndex, deadlineTicks, noActionStreak });

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
}
