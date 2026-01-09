using System.Text.Json;
using Combats.Battle.Application.Abstractions;
using Combats.Battle.Application.ReadModels;
using Combats.Battle.Domain.Model;
using Combats.Battle.Infrastructure.State.Redis.Mapping;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Combats.Battle.Infrastructure.State.Redis;

/// <summary>
/// Infrastructure implementation of IBattleStateStore using Redis.
/// Maps between Infrastructure BattleState and Domain/Application models.
/// </summary>
public class RedisBattleStateStore : IBattleStateStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisBattleStateStore> _logger;
    private const string StateKeyPrefix = "battle:state:";
    private const string ActionKeyPrefix = "battle:action:";
    private const string ActiveBattlesSetKey = "battle:active";
    private const string DeadlinesZSetKey = "battle:deadlines";

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

    public async Task<bool> TryInitializeBattleAsync(
        Guid battleId, 
        BattleDomainState initialState, 
        CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = GetStateKey(battleId);

        // Convert Domain state to Infrastructure storage model
        var deadlineUtc = DateTime.UtcNow; // ArenaOpen deadline is meaningless but consistent
        var state = StoredStateMapper.FromDomainState(initialState, deadlineUtc, version: 1);

        // Use SETNX for idempotent initialization
        var json = JsonSerializer.Serialize(state);
        var setResult = await db.StringSetAsync(key, json, when: When.NotExists);

        if (setResult)
        {
            // Add to active battles set
            await db.SetAddAsync(ActiveBattlesSetKey, battleId.ToString());
            _logger.LogInformation(
                "Initialized battle state for BattleId: {BattleId}, Phase: {Phase}",
                battleId, state.Phase);
        }
        else
        {
            _logger.LogInformation(
                "Battle {BattleId} already initialized, skipping (idempotent)",
                battleId);
        }

        return setResult;
    }

    public async Task<BattleSnapshot?> GetStateAsync(Guid battleId, CancellationToken cancellationToken = default)
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
            return StoredStateMapper.ToSnapshot(state);
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
        // Atomic: update state + add to deadlines ZSET in single Lua script
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
            -- Add to deadlines ZSET atomically
            redis.call('ZADD', KEYS[2], ARGV[2], ARGV[3])
            return 1
        ";

        var result = await db.ScriptEvaluateAsync(
            script,
            new RedisKey[] { key, DeadlinesZSetKey },
            new RedisValue[] { turnIndex, deadlineTicks, battleId.ToString() });

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
        int playerAHp,
        int playerBHp,
        CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = GetStateKey(battleId);

        var deadlineTicks = nextDeadlineUtc.ToUniversalTime().Ticks;
        // Phase enum: ArenaOpen=0, TurnOpen=1, Resolving=2, Ended=3
        // Atomic: update state + HP + deadlines ZSET in single Lua script
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
            -- Update HP atomically with turn resolution
            state.PlayerAHp = tonumber(ARGV[5])
            state.PlayerBHp = tonumber(ARGV[6])
            -- Clear NextResolveScheduledUtcTicks (deprecated, kept for backward compatibility)
            state.NextResolveScheduledUtcTicks = 0
            state.Version = state.Version + 1
            redis.call('SET', KEYS[1], cjson.encode(state))
            -- Update deadlines ZSET atomically
            redis.call('ZADD', KEYS[2], ARGV[3], ARGV[7])
            return 1
        ";

        var result = await db.ScriptEvaluateAsync(
            script,
            new RedisKey[] { key, DeadlinesZSetKey },
            new RedisValue[] { 
                currentTurnIndex, 
                nextTurnIndex, 
                deadlineTicks, 
                noActionStreak,
                playerAHp,
                playerBHp,
                battleId.ToString()
            });

        var success = (int)result == 1;
        if (success)
        {
            _logger.LogInformation(
                "Resolved turn {CurrentTurnIndex} and opened turn {NextTurnIndex} for BattleId: {BattleId} with HP A:{PlayerAHp} B:{PlayerBHp}",
                currentTurnIndex, nextTurnIndex, battleId, playerAHp, playerBHp);
        }

        return success;
    }

    public async Task<EndBattleCommitResult> EndBattleAndMarkResolvedAsync(
        Guid battleId, 
        int turnIndex, 
        int noActionStreak,
        int playerAHp,
        int playerBHp,
        CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = GetStateKey(battleId);

        // Phase enum: ArenaOpen=0, TurnOpen=1, Resolving=2, Ended=3
        // Atomic: set Phase=Ended + LastResolvedTurnIndex + NoActionStreakBoth + HP + remove from deadlines ZSET
        // Returns: 2 = AlreadyEnded, 1 = EndedNow, 0 = NotCommitted
        const string script = @"
            local stateJson = redis.call('GET', KEYS[1])
            if not stateJson then
                return 0
            end
            local state = cjson.decode(stateJson)
            -- If already ended (Ended=3), return AlreadyEnded (2)
            if state.Phase == 3 then
                return 2
            end
            -- Only end if currently resolving (Resolving=2) the specified turn
            if state.Phase ~= 2 or state.TurnIndex ~= tonumber(ARGV[1]) then
                return 0
            end
            -- Set to Ended (3)
            state.Phase = 3
            state.LastResolvedTurnIndex = tonumber(ARGV[1])
            state.NoActionStreakBoth = tonumber(ARGV[2])
            -- Update HP atomically with battle end
            state.PlayerAHp = tonumber(ARGV[3])
            state.PlayerBHp = tonumber(ARGV[4])
            state.NextResolveScheduledUtcTicks = 0
            state.Version = state.Version + 1
            redis.call('SET', KEYS[1], cjson.encode(state))
            -- Remove from active battles set
            redis.call('SREM', KEYS[2], ARGV[5])
            -- Remove from deadlines ZSET atomically
            redis.call('ZREM', KEYS[3], ARGV[5])
            return 1
        ";

        var result = await db.ScriptEvaluateAsync(
            script,
            new RedisKey[] { key, ActiveBattlesSetKey, DeadlinesZSetKey },
            new RedisValue[] { turnIndex, noActionStreak, playerAHp, playerBHp, battleId.ToString() });

        var resultCode = (int)result;
        var commitResult = (EndBattleCommitResult)resultCode;

        if (commitResult == EndBattleCommitResult.EndedNow)
        {
            _logger.LogInformation(
                "Ended battle and marked turn {TurnIndex} resolved for BattleId: {BattleId} with HP A:{PlayerAHp} B:{PlayerBHp}",
                turnIndex, battleId, playerAHp, playerBHp);
        }
        else if (commitResult == EndBattleCommitResult.AlreadyEnded)
        {
            _logger.LogInformation(
                "Battle {BattleId} already ended (idempotent EndBattleAndMarkResolvedAsync call)",
                battleId);
        }

        return commitResult;
    }

    // Deadline index methods (Redis ZSET)
    public async Task AddBattleDeadlineAsync(Guid battleId, DateTime deadlineUtc, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var deadlineTicks = deadlineUtc.ToUniversalTime().Ticks;
        
        await db.SortedSetAddAsync(DeadlinesZSetKey, battleId.ToString(), deadlineTicks);
        
        _logger.LogInformation(
            "Added battle deadline for BattleId: {BattleId}, DeadlineUtc: {DeadlineUtc}",
            battleId, deadlineUtc);
    }

    public async Task RemoveBattleDeadlineAsync(Guid battleId, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        
        await db.SortedSetRemoveAsync(DeadlinesZSetKey, battleId.ToString());
        
        _logger.LogInformation(
            "Removed battle deadline for BattleId: {BattleId}",
            battleId);
    }

    public async Task<List<Guid>> GetDueBattlesAsync(DateTime nowUtc, int limit, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var nowTicks = nowUtc.ToUniversalTime().Ticks;
        
        // ZRANGEBYSCORE battle:deadlines -inf nowTicks LIMIT 0 limit
        var members = await db.SortedSetRangeByScoreAsync(
            DeadlinesZSetKey,
            stop: nowTicks,
            take: limit);
        
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

    public async Task<DateTime?> GetNextDeadlineUtcAsync(CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        
        // ZRANGE battle:deadlines 0 0 WITHSCORES - get the first (lowest score) element with its score
        var result = await db.SortedSetRangeByRankWithScoresAsync(
            DeadlinesZSetKey,
            start: 0,
            stop: 0,
            order: Order.Ascending);
        
        if (result == null || result.Length == 0)
        {
            return null;
        }
        
        var score = result[0].Score;
        // Score is stored as ticks (long)
        return new DateTime((long)score, DateTimeKind.Utc);
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

    public async Task<ActionStoreResult> StoreActionAsync(Guid battleId, int turnIndex, Guid playerId, string actionPayload, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = GetActionKey(battleId, turnIndex, playerId);

        // Store action with expiration (cleanup after battle ends)
        // Use SET NX (When.NotExists) to ensure first-write-wins
        var wasSet = await db.StringSetAsync(key, actionPayload, TimeSpan.FromHours(1), When.NotExists);

        if (wasSet)
        {
            _logger.LogInformation(
                "Stored action for BattleId: {BattleId}, TurnIndex: {TurnIndex}, PlayerId: {PlayerId}",
                battleId, turnIndex, playerId);
            return ActionStoreResult.Accepted;
        }
        else
        {
            _logger.LogInformation(
                "Action already submitted for BattleId: {BattleId}, TurnIndex: {TurnIndex}, PlayerId: {PlayerId}",
                battleId, turnIndex, playerId);
            return ActionStoreResult.AlreadySubmitted;
        }
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


