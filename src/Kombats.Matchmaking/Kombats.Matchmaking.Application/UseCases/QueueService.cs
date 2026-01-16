using Kombats.Matchmaking.Application.Abstractions;
using Kombats.Matchmaking.Domain;
using Microsoft.Extensions.Logging;

namespace Kombats.Matchmaking.Application.UseCases;

/// <summary>
/// Application service for queue operations (join/leave/status).
/// </summary>
public class QueueService
{
    private readonly IMatchQueueStore _queueStore;
    private readonly IPlayerMatchStatusStore _statusStore;
    private readonly IMatchRepository _matchRepository;
    private readonly ILogger<QueueService> _logger;

    public QueueService(
        IMatchQueueStore queueStore,
        IPlayerMatchStatusStore statusStore,
        IMatchRepository matchRepository,
        ILogger<QueueService> logger)
    {
        _queueStore = queueStore;
        _statusStore = statusStore;
        _matchRepository = matchRepository;
        _logger = logger;
    }

    /// <summary>
    /// Joins a player to the matchmaking queue.
    /// First checks Postgres for active match (source of truth).
    /// Returns the current status (Searching if added, Matched if already matched).
    /// </summary>
    public async Task<PlayerMatchStatus> JoinQueueAsync(Guid playerId, string variant, CancellationToken cancellationToken = default)
    {
        // CRITICAL: Check Postgres first (source of truth) for active match
        // If player has an active match, return Matched and DO NOT add to queue
        var activeMatch = await _matchRepository.GetLatestForPlayerAsync(playerId, cancellationToken);
        if (activeMatch != null && IsActiveMatch(activeMatch.State))
        {
            _logger.LogInformation(
                "Player has active match in Postgres (cannot join queue): PlayerId={PlayerId}, MatchId={MatchId}, BattleId={BattleId}, State={State}, Variant={Variant}",
                playerId, activeMatch.MatchId, activeMatch.BattleId, activeMatch.State, activeMatch.Variant);
            return new PlayerMatchStatus
            {
                State = PlayerMatchState.Matched,
                MatchId = activeMatch.MatchId,
                BattleId = activeMatch.BattleId,
                Variant = activeMatch.Variant,
                UpdatedAtUtc = activeMatch.UpdatedAtUtc,
                MatchState = activeMatch.State
            };
        }

        // Check Redis status for idempotency
        var currentStatus = await _statusStore.GetStatusAsync(playerId, cancellationToken);
        
        if (currentStatus != null)
        {
            // Already searching - ensure they're also in queue (defensive check)
            if (currentStatus.State == PlayerMatchState.Searching)
            {
                // Check if variant matches
                if (currentStatus.Variant != variant)
                {
                    _logger.LogWarning(
                        "Player is searching with different variant: PlayerId={PlayerId}, CurrentVariant={CurrentVariant}, RequestedVariant={RequestedVariant}",
                        playerId, currentStatus.Variant, variant);
                    // For now, return current status (don't allow switching variant while searching)
                    // TODO: Consider implementing leave old + join new atomically if needed
                    return currentStatus;
                }

                // Try to join queue anyway (idempotent - will return false if already queued)
                var added = await _queueStore.TryJoinQueueAsync(variant, playerId, cancellationToken);
                _logger.LogInformation(
                    "Player already searching (idempotent join): PlayerId={PlayerId}, Variant={Variant}, AddedToQueue={AddedToQueue}",
                    playerId, currentStatus.Variant, added);
                return currentStatus;
            }
        }

        // CRITICAL: Both operations must happen for a player to be matchable
        // 1. Enqueue player into match queue (mm:queue/mm:queued)
        bool isAdded = await _queueStore.TryJoinQueueAsync(variant, playerId, cancellationToken);
        
        // 2. Set player status to Searching (mm:player:*)
        // Always set status regardless of queue result to maintain consistency
        // If already in queue (added=false), status update is still needed for idempotency
        await _statusStore.SetSearchingAsync(variant, playerId, cancellationToken);
        
        _logger.LogInformation(
            "Player joined matchmaking: PlayerId={PlayerId}, Variant={Variant}, AddedToQueue={AddedToQueue}",
            playerId, variant, isAdded);
        
        return new PlayerMatchStatus
        {
            State = PlayerMatchState.Searching,
            MatchId = null,
            BattleId = null,
            Variant = variant,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Removes a player from the matchmaking queue.
    /// First checks Postgres for active match (source of truth).
    /// Returns result indicating success, not in queue, or already matched (conflict).
    /// </summary>
    public async Task<LeaveQueueResult> LeaveQueueAsync(Guid playerId, string variant, CancellationToken cancellationToken = default)
    {
        // CRITICAL: Check Postgres first (source of truth) for active match
        // If player has an active match, return AlreadyMatched
        var activeMatch = await _matchRepository.GetLatestForPlayerAsync(playerId, cancellationToken);
        if (activeMatch != null && IsActiveMatch(activeMatch.State))
        {
            _logger.LogWarning(
                "Player attempted to leave but has active match in Postgres: PlayerId={PlayerId}, MatchId={MatchId}, BattleId={BattleId}, State={State}, Variant={Variant}",
                playerId, activeMatch.MatchId, activeMatch.BattleId, activeMatch.State, activeMatch.Variant);
            return LeaveQueueResult.AlreadyMatched(new MatchInfo
            {
                MatchId = activeMatch.MatchId,
                BattleId = activeMatch.BattleId
            });
        }

        // Check Redis status
        var currentStatus = await _statusStore.GetStatusAsync(playerId, cancellationToken);
        
        if (currentStatus == null)
        {
            // Not in queue - idempotent success
            _logger.LogInformation(
                "Player {PlayerId} not in queue (idempotent leave)",
                playerId);
            return LeaveQueueResult.NotInQueue;
        }

        // Remove from queue and status (idempotent operations)
        await _queueStore.TryLeaveQueueAsync(variant, playerId, cancellationToken);
        await _statusStore.RemoveStatusAsync(playerId, cancellationToken);
        
        _logger.LogInformation(
            "Player left queue: PlayerId={PlayerId}, Variant={Variant}",
            playerId, variant);
        
        return LeaveQueueResult.LeftSuccessfully;
    }

    /// <summary>
    /// Gets the current match status for a player.
    /// First checks Postgres for latest match (source of truth).
    /// If no active match found, checks Redis for queue status (Searching/NotQueued).
    /// </summary>
    public async Task<PlayerMatchStatus?> GetStatusAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        // First check Postgres for latest match (source of truth)
        var match = await _matchRepository.GetLatestForPlayerAsync(playerId, cancellationToken);
        
        if (match != null && IsActiveMatch(match.State))
        {
            // Player has an active match in Postgres - return Matched status with match state
            return new PlayerMatchStatus
            {
                State = PlayerMatchState.Matched,
                MatchId = match.MatchId,
                BattleId = match.BattleId,
                Variant = match.Variant,
                UpdatedAtUtc = match.UpdatedAtUtc,
                MatchState = match.State
            };
        }

        // No active match in Postgres - check Redis for queue status
        // Check if player is in the queued set (Searching)
        var redisStatus = await _statusStore.GetStatusAsync(playerId, cancellationToken);
        
        if (redisStatus != null && redisStatus.State == PlayerMatchState.Searching)
        {
            return redisStatus;
        }

        // Not matched and not searching
        return null;
    }

    /// <summary>
    /// Determines if a match state represents an active match (not completed or timed out).
    /// </summary>
    private static bool IsActiveMatch(MatchState state)
    {
        return state != MatchState.Completed && state != MatchState.TimedOut;
    }
}

/// <summary>
/// Result of leave queue operation.
/// </summary>
public class LeaveQueueResult
{
    public required LeaveQueueResultType Type { get; init; }
    public MatchInfo? MatchInfo { get; init; }

    public static LeaveQueueResult LeftSuccessfully => new() { Type = LeaveQueueResultType.LeftSuccessfully };
    public static LeaveQueueResult NotInQueue => new() { Type = LeaveQueueResultType.NotInQueue };
    public static LeaveQueueResult AlreadyMatched(MatchInfo matchInfo) => new() 
    { 
        Type = LeaveQueueResultType.AlreadyMatched, 
        MatchInfo = matchInfo 
    };
}

public enum LeaveQueueResultType
{
    LeftSuccessfully,
    NotInQueue,
    AlreadyMatched
}

public class MatchInfo
{
    public required Guid MatchId { get; init; }
    public required Guid BattleId { get; init; }
}

