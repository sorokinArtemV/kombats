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
    /// Returns the current status (Searching if added, Matched if already matched).
    /// </summary>
    public async Task<PlayerMatchStatus> JoinQueueAsync(Guid playerId, string variant, CancellationToken cancellationToken = default)
    {
        // Check current status first (idempotency)
        var currentStatus = await _statusStore.GetStatusAsync(playerId, cancellationToken);
        
        if (currentStatus != null)
        {
            // Already searching or matched - return current status
            if (currentStatus.State == PlayerMatchState.Matched)
            {
                _logger.LogInformation(
                    "Player already matched: PlayerId={PlayerId}, MatchId={MatchId}, BattleId={BattleId}, Variant={Variant}",
                    playerId, currentStatus.MatchId, currentStatus.BattleId, currentStatus.Variant);
                return currentStatus;
            }
            
            // Already searching - ensure they're also in queue (defensive check)
            if (currentStatus.State == PlayerMatchState.Searching)
            {
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
    /// Returns null if not in queue, or MatchInfo if already matched (conflict).
    /// </summary>
    public async Task<LeaveQueueResult> LeaveQueueAsync(Guid playerId, string variant, CancellationToken cancellationToken = default)
    {
        // Check current status first
        var currentStatus = await _statusStore.GetStatusAsync(playerId, cancellationToken);
        
        if (currentStatus == null)
        {
            // Not in queue - idempotent success
            _logger.LogInformation(
                "Player {PlayerId} not in queue (idempotent leave)",
                playerId);
            return LeaveQueueResult.NotInQueue;
        }

        if (currentStatus.State == PlayerMatchState.Matched)
        {
            // Already matched - return conflict
            _logger.LogWarning(
                "Player attempted to leave but already matched: PlayerId={PlayerId}, MatchId={MatchId}, BattleId={BattleId}, Variant={Variant}",
                playerId, currentStatus.MatchId, currentStatus.BattleId, currentStatus.Variant);
            return LeaveQueueResult.AlreadyMatched(new MatchInfo
            {
                MatchId = currentStatus.MatchId!.Value,
                BattleId = currentStatus.BattleId!.Value
            });
        }

        // Remove from queue and status
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
    /// If no match found, checks Redis for queue status (Searching/NotQueued).
    /// </summary>
    public async Task<PlayerMatchStatus?> GetStatusAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        // First check Postgres for latest match (source of truth)
        var match = await _matchRepository.GetLatestForPlayerAsync(playerId, cancellationToken);
        
        if (match != null && match.State >= MatchState.Created)
        {
            // Player has a match in Postgres - return Matched status with match state
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

        // No match in Postgres - check Redis for queue status
        // Check if player is in the queued set (Searching)
        var redisStatus = await _statusStore.GetStatusAsync(playerId, cancellationToken);
        
        if (redisStatus != null && redisStatus.State == PlayerMatchState.Searching)
        {
            return redisStatus;
        }

        // Not matched and not searching
        return null;
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

