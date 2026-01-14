using System;
using System.Text.Json;
using Kombats.Battle.Application.Abstractions;
using Kombats.Battle.Application.ReadModels;
using Kombats.Battle.Domain.Model;
using Kombats.Battle.Infrastructure.State.Redis.Mapping;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Kombats.Battle.Infrastructure.State.Redis;

/// <summary>
/// Infrastructure implementation of IBattleStateStore using Redis.
/// Maps between Infrastructure BattleState and Domain/Application models.
/// 
/// Production scheduling: TurnDeadlineWorker uses ClaimDueBattlesAsync in a tick loop with adaptive backoff.
/// Do not use legacy methods (GetNextDeadlineUtcAsync, GetDueBattlesAsync) for production code.
/// </summary>
public class RedisBattleStateStore : IBattleStateStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisBattleStateStore> _logger;
    private readonly BattleRedisOptions _options;
    private readonly IClock _clock;
    
    private const string StateKeyPrefix = "battle:state:";
    private const string ActionKeyPrefix = "battle:action:";
    private const string ActiveBattlesSetKey = "battle:active";
    private const string DeadlinesZSetKey = "battle:deadlines";

    // ClaimDueBattles script constants (passed as ARGV)
    private const int SmallDelayMs = 200; // Delay for non-TurnOpen phases

    public RedisBattleStateStore(
        IConnectionMultiplexer redis,
        ILogger<RedisBattleStateStore> logger,
        IOptions<BattleRedisOptions> options,
        IClock clock)
    {
        _redis = redis;
        _logger = logger;
        _options = options.Value;
        _clock = clock;
    }
    
    /// <summary>
    /// Helper method for unix milliseconds conversion (ZSET scores and state JSON use unixMs for consistency)
    /// </summary>
    private static long ToUnixMs(DateTime utc) => new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeMilliseconds();
    private string GetStateKey(Guid battleId) => $"{StateKeyPrefix}{battleId}";
    private string GetActionKey(Guid battleId, int turnIndex, Guid playerId) => $"{ActionKeyPrefix}{battleId}:turn:{turnIndex}:player:{playerId}";
    private string GetLockKey(Guid battleId, int turnIndex) => $"lock:battle:{battleId}:turn:{turnIndex}";

    public async Task<bool> TryInitializeBattleAsync(
        Guid battleId, 
        BattleDomainState initialState, 
        CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        string key = GetStateKey(battleId);

        // Convert Domain state to Infrastructure storage model
        DateTime deadlineUtc = _clock.UtcNow; // ArenaOpen deadline is meaningless but consistent
        BattleState state = StoredStateMapper.FromDomainState(initialState, deadlineUtc, version: 1);

        // Use SETNX for idempotent initialization
        string json = JsonSerializer.Serialize(state);
        bool setResult = await db.StringSetAsync(key, json, when: When.NotExists);

        if (setResult)
        {
            // Add to active battles set
            await db.SetAddAsync(ActiveBattlesSetKey, battleId.ToString());
            _logger.LogInformation("Initialized battle state for BattleId: {BattleId}, Phase: {Phase}", battleId, state.Phase);
        }
        else
        {
            _logger.LogInformation("Battle {BattleId} already initialized, skipping (idempotent)", battleId);
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
            BattleState? state = JsonSerializer.Deserialize<BattleState>(json.ToString());
            if (state == null)
            {
                _logger.LogError("Deserialized battle state is null for BattleId: {BattleId}. This indicates a serialization mismatch.", battleId);
                throw new InvalidOperationException($"Deserialized battle state is null for BattleId: {battleId}");
            }
            
            return StoredStateMapper.ToSnapshot(state);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize battle state for BattleId: {BattleId}. JSON may be corrupted or schema changed.", battleId);
            throw new InvalidOperationException($"Failed to deserialize battle state for BattleId: {battleId}", ex);
        }
    }

    public async Task<bool> TryOpenTurnAsync(Guid battleId, int turnIndex, DateTime deadlineUtc, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = GetStateKey(battleId);

        // Convert deadline to unix milliseconds for ZSET score (avoids double precision issues)
        var deadlineUnixMs = ToUnixMs(deadlineUtc);

        var result = await db.ScriptEvaluateAsync(
            RedisScripts.TryOpenTurnScript,
            [key, DeadlinesZSetKey],
            [turnIndex, deadlineUnixMs, battleId.ToString()]);

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

        var result = await db.ScriptEvaluateAsync(
            RedisScripts.TryMarkTurnResolvingScript,
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

        // Convert deadline to unix milliseconds for ZSET score (avoids double precision issues)
        var deadlineUnixMs = ToUnixMs(nextDeadlineUtc);

        var result = await db.ScriptEvaluateAsync(
            RedisScripts.MarkTurnResolvedAndOpenNextScript,
            [key, DeadlinesZSetKey],
            [
                currentTurnIndex, 
                nextTurnIndex, 
                deadlineUnixMs, 
                noActionStreak,
                playerAHp,
                playerBHp,
                battleId.ToString()
            ]);

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

        var result = await db.ScriptEvaluateAsync(
            RedisScripts.EndBattleAndMarkResolvedScript,
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
    
    public async Task<IReadOnlyList<ClaimedBattleDue>> ClaimDueBattlesAsync(
        DateTime nowUtc, 
        int limit, 
        TimeSpan leaseTtl, 
        CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        // Convert to unix milliseconds for ZSET score (avoids double precision issues)
        var nowUnixMs = ToUnixMs(nowUtc);
        var leaseWindowMs = (int)leaseTtl.TotalMilliseconds;

        try
        {
            var result = await db.ScriptEvaluateAsync(
                RedisScripts.ClaimDueBattlesScript,
                new RedisKey[] { DeadlinesZSetKey },
                new RedisValue[] { nowUnixMs, limit, leaseWindowMs, SmallDelayMs, StateKeyPrefix });

            var claimed = new List<ClaimedBattleDue>();
            
            if (!result.IsNull)
            {
                var results = (RedisValue[]?)result;
                if (results != null)
                {
                    // Results come as pairs: [battleId1, turnIndex1, battleId2, turnIndex2, ...]
                    for (int i = 0; i < results.Length; i += 2)
                    {
                        if (i + 1 < results.Length)
                        {
                            var battleIdStr = results[i].ToString();
                            var turnIndexStr = results[i + 1].ToString();
                        
                            if (Guid.TryParse(battleIdStr, out var battleId) && 
                                int.TryParse(turnIndexStr, out var turnIndex))
                            {
                                claimed.Add(new ClaimedBattleDue
                                {
                                    BattleId = battleId,
                                    TurnIndex = turnIndex
                                });
                            }
                        }
                    }
                }
            }

            if (claimed.Count > 0)
            {
                _logger.LogDebug(
                    "Claimed {Count} battles for deadline resolution",
                    claimed.Count);
            }

            return claimed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error claiming due battles from Redis. NowUtc: {NowUtc}, Limit: {Limit}, LeaseTtl: {LeaseTtl}",
                nowUtc, limit, leaseTtl);
            throw;
        }
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

        // Store action with configurable expiration (cleanup after battle ends)
        // Use SET NX (When.NotExists) to ensure first-write-wins
        var wasSet = await db.StringSetAsync(key, actionPayload, _options.ActionTtl, When.NotExists);

        if (wasSet)
        {
            _logger.LogDebug(
                "Stored action for BattleId: {BattleId}, TurnIndex: {TurnIndex}, PlayerId: {PlayerId}",
                battleId, turnIndex, playerId);
            return ActionStoreResult.Accepted;
        }
        else
        {
            _logger.LogDebug(
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


