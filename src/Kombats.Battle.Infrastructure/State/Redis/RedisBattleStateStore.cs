using System;
using System.Text.Json;
using Kombats.Battle.Application.Abstractions;
using Kombats.Battle.Application.ReadModels;
using Kombats.Battle.Application.UseCases.Turns;
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

    /// <summary>
    /// Centralized JSON serializer options for PlayerActionCommand.
    /// Uses camelCase for writes and case-insensitive reads for backward compatibility.
    /// </summary>
    private static readonly JsonSerializerOptions PlayerActionCommandJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

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
    private static long ToUnixMs(DateTimeOffset value) => value.ToUnixTimeMilliseconds();
    private string GetStateKey(Guid battleId) => $"{StateKeyPrefix}{battleId}";
    private string GetActionKey(Guid battleId, int turnIndex, Guid playerId) => $"{ActionKeyPrefix}{battleId}:turn:{turnIndex}:player:{playerId}";
    private string GetLockKey(Guid battleId, int turnIndex) => $"lock:battle:{battleId}:turn:{turnIndex}";
    private string GetSubmissionMarkerKey(Guid battleId, int turnIndex) => $"battle:turn:{battleId}:{turnIndex}:submitted";

    public async Task<bool> TryInitializeBattleAsync(
        Guid battleId, 
        BattleDomainState initialState, 
        CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        string key = GetStateKey(battleId);

        // Convert Domain state to Infrastructure storage model
        DateTimeOffset deadlineUtc = _clock.UtcNow; // ArenaOpen deadline is meaningless but consistent
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

    public async Task<bool> TryOpenTurnAsync(Guid battleId, int turnIndex, DateTimeOffset deadlineUtc, CancellationToken cancellationToken = default)
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
        DateTimeOffset nextDeadlineUtc,
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
        DateTimeOffset nowUtc, 
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

    public async Task<ActionStoreResult> StoreActionAsync(Guid battleId, int turnIndex, Guid playerId, PlayerActionCommand actionCommand, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = GetActionKey(battleId, turnIndex, playerId);

        // Serialize canonical action to JSON using centralized options
        var serializedAction = JsonSerializer.Serialize(actionCommand, PlayerActionCommandJsonOptions);

        // Store action with configurable expiration (cleanup after battle ends)
        // Use SET NX (When.NotExists) to ensure first-write-wins
        var wasSet = await db.StringSetAsync(key, serializedAction, _options.ActionTtl, When.NotExists);

        if (wasSet)
        {
            _logger.LogDebug(
                "Stored action for BattleId: {BattleId}, TurnIndex: {TurnIndex}, PlayerId: {PlayerId}, Quality: {Quality}",
                battleId, turnIndex, playerId, actionCommand.Quality);
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

    public async Task<(PlayerActionCommand? PlayerAAction, PlayerActionCommand? PlayerBAction)> GetActionsAsync(
        Guid battleId,
        int turnIndex,
        Guid playerAId,
        Guid playerBId,
        CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var keyA = GetActionKey(battleId, turnIndex, playerAId);
        var keyB = GetActionKey(battleId, turnIndex, playerBId);

        var actionAValue = await db.StringGetAsync(keyA);
        var actionBValue = await db.StringGetAsync(keyB);

        PlayerActionCommand? playerAAction = null;
        PlayerActionCommand? playerBAction = null;

        if (actionAValue.HasValue)
        {
            playerAAction = DeserializeActionWithLegacySupport(
                actionAValue.ToString(),
                battleId,
                turnIndex,
                playerAId,
                "PlayerA");
        }

        if (actionBValue.HasValue)
        {
            playerBAction = DeserializeActionWithLegacySupport(
                actionBValue.ToString(),
                battleId,
                turnIndex,
                playerBId,
                "PlayerB");
        }

        return (playerAAction, playerBAction);
    }

    public async Task<ActionStoreAndCheckResult> StoreActionAndCheckBothSubmittedAsync(
        Guid battleId,
        int turnIndex,
        Guid playerId,
        Guid playerAId,
        Guid playerBId,
        PlayerActionCommand actionCommand,
        CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var actionKey = GetActionKey(battleId, turnIndex, playerId);
        var submissionMarkerKey = GetSubmissionMarkerKey(battleId, turnIndex);

        // Determine player role ("A" or "B")
        var playerRole = playerId == playerAId ? "A" : (playerId == playerBId ? "B" : throw new InvalidOperationException($"Player {playerId} is not a participant in battle {battleId}"));

        // Serialize canonical action to JSON using centralized options
        var serializedAction = JsonSerializer.Serialize(actionCommand, PlayerActionCommandJsonOptions);

        // Convert TTL to seconds (Redis EX expects seconds)
        var ttlSeconds = (int)_options.ActionTtl.TotalSeconds;

        try
        {
            var result = await db.ScriptEvaluateAsync(
                RedisScripts.StoreActionAndCheckBothSubmittedScript,
                [actionKey, submissionMarkerKey],
                [serializedAction, ttlSeconds, playerRole]);

            if (result.IsNull)
            {
                _logger.LogError(
                    "StoreActionAndCheckBothSubmittedScript returned null for BattleId: {BattleId}, TurnIndex: {TurnIndex}, PlayerId: {PlayerId}",
                    battleId, turnIndex, playerId);
                throw new InvalidOperationException("Redis script returned null result");
            }

            var results = (RedisValue[]?)result;
            if (results == null || results.Length < 3)
            {
                _logger.LogError(
                    "StoreActionAndCheckBothSubmittedScript returned invalid result format for BattleId: {BattleId}, TurnIndex: {TurnIndex}, PlayerId: {PlayerId}",
                    battleId, turnIndex, playerId);
                throw new InvalidOperationException("Redis script returned invalid result format");
            }

            var alreadySubmitted = (int)results[0] == 1;
            var bothSubmitted = (int)results[1] == 1;
            var wasStored = (int)results[2] == 1;

            var storeResult = alreadySubmitted ? ActionStoreResult.AlreadySubmitted : ActionStoreResult.Accepted;

            if (wasStored)
            {
                _logger.LogDebug(
                    "Stored action atomically for BattleId: {BattleId}, TurnIndex: {TurnIndex}, PlayerId: {PlayerId}, Quality: {Quality}, BothSubmitted: {BothSubmitted}",
                    battleId, turnIndex, playerId, actionCommand.Quality, bothSubmitted);
            }
            else
            {
                _logger.LogDebug(
                    "Action already submitted for BattleId: {BattleId}, TurnIndex: {TurnIndex}, PlayerId: {PlayerId}, BothSubmitted: {BothSubmitted}",
                    battleId, turnIndex, playerId, bothSubmitted);
            }

            return new ActionStoreAndCheckResult
            {
                StoreResult = storeResult,
                BothSubmitted = bothSubmitted,
                WasStored = wasStored
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error in StoreActionAndCheckBothSubmittedAsync for BattleId: {BattleId}, TurnIndex: {TurnIndex}, PlayerId: {PlayerId}",
                battleId, turnIndex, playerId);
            throw;
        }
    }

    /// <summary>
    /// Deserializes an action with backward compatibility for legacy formats.
    /// First attempts to deserialize as canonical PlayerActionCommand.
    /// If that fails, attempts to parse as legacy format and converts to NoAction.
    /// </summary>
    private PlayerActionCommand? DeserializeActionWithLegacySupport(
        string storedValue,
        Guid battleId,
        int turnIndex,
        Guid playerId,
        string playerLabel)
    {
        // First attempt: deserialize as canonical PlayerActionCommand
        try
        {
            var command = JsonSerializer.Deserialize<PlayerActionCommand>(
                storedValue,
                PlayerActionCommandJsonOptions);
            
            if (command != null)
            {
                // Validate invariant
                try
                {
                    command.ValidateInvariant();
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogError(ex,
                        "Stored action violates invariant for BattleId: {BattleId}, TurnIndex: {TurnIndex}, PlayerId: {PlayerId}. Converting to NoAction.",
                        battleId, turnIndex, playerId);
                    // Convert to NoAction with InvariantViolation
                    return new PlayerActionCommand
                    {
                        BattleId = battleId,
                        PlayerId = playerId,
                        TurnIndex = turnIndex,
                        AttackZone = null,
                        BlockZonePrimary = null,
                        BlockZoneSecondary = null,
                        Quality = ActionQuality.Invalid,
                        RejectReason = ActionRejectReason.InvariantViolation
                    };
                }
                return command;
            }
        }
        catch (JsonException)
        {
            // Not valid canonical JSON - try legacy format handling
        }

        // Second attempt: legacy format - try to parse as simple JSON with attackZone
        // This handles cases where old format stored raw client payload
        try
        {
            using var doc = JsonDocument.Parse(storedValue);
            var root = doc.RootElement;
            
            // If it looks like a legacy action payload (has attackZone property), 
            // treat as corrupted and return NoAction
            if (root.TryGetProperty("attackZone", out _))
            {
                _logger.LogError(
                    "Legacy action format detected for BattleId: {BattleId}, TurnIndex: {TurnIndex}, PlayerId: {PlayerId}. " +
                    "Stored value appears to be raw client payload. Converting to NoAction with CorruptedStoredAction reason.",
                    battleId, turnIndex, playerId);
                
                // Return NoAction - we can't safely convert without battle state for full validation
                // This preserves gameplay: missing/corrupted actions become NoAction
                return new PlayerActionCommand
                {
                    BattleId = battleId,
                    PlayerId = playerId,
                    TurnIndex = turnIndex,
                    AttackZone = null,
                    BlockZonePrimary = null,
                    BlockZoneSecondary = null,
                    Quality = ActionQuality.Invalid,
                    RejectReason = ActionRejectReason.InvalidJson // Use InvalidJson as closest match
                };
            }
        }
        catch (JsonException)
        {
            // Not valid JSON at all
        }

        // Final fallback: completely unparseable - treat as corrupted
        _logger.LogError(
            "Failed to deserialize stored action (corrupted or legacy format) for BattleId: {BattleId}, TurnIndex: {TurnIndex}, PlayerId: {PlayerId}. " +
            "Stored value: {StoredValue}. Converting to NoAction.",
            battleId, turnIndex, playerId, storedValue.Length > 100 ? storedValue.Substring(0, 100) + "..." : storedValue);
        
        // Return NoAction to preserve gameplay semantics
        return new PlayerActionCommand
        {
            BattleId = battleId,
            PlayerId = playerId,
            TurnIndex = turnIndex,
            AttackZone = null,
            BlockZonePrimary = null,
            BlockZoneSecondary = null,
            Quality = ActionQuality.Invalid,
            RejectReason = ActionRejectReason.InvalidJson
        };
    }
}


