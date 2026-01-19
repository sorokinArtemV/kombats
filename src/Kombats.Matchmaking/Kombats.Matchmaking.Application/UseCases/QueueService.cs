using Kombats.Matchmaking.Application.Abstractions;
using Kombats.Matchmaking.Application.Options;
using Kombats.Matchmaking.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kombats.Matchmaking.Application.UseCases;

/// <summary>
/// Application service for queue operations (join/leave/status).
/// </summary>
public class QueueService
{
    private readonly IMatchQueueStore _queueStore;
    private readonly IMatchRepository _matchRepository;
    private readonly ILogger<QueueService> _logger;
    private readonly QueueOptions _options;

    public QueueService(
        IMatchQueueStore queueStore,
        IMatchRepository matchRepository,
        ILogger<QueueService> logger,
        IOptions<QueueOptions> options)
    {
        _queueStore = queueStore;
        _matchRepository = matchRepository;
        _logger = logger;
        _options = options.Value;
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

        // Enqueue player into match queue (idempotent operation - TryJoinQueueAsync handles already-queued case)
        bool isAdded = await _queueStore.TryJoinQueueAsync(variant, playerId, cancellationToken);
        
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

        // Check if player is in queue
        var isQueued = await _queueStore.IsQueuedAsync(variant, playerId, cancellationToken);
        
        if (!isQueued)
        {
            // Not in queue - idempotent success
            _logger.LogInformation(
                "Player {PlayerId} not in queue (idempotent leave)",
                playerId);
            return LeaveQueueResult.NotInQueue;
        }

        // Remove from queue (idempotent operation)
        await _queueStore.TryLeaveQueueAsync(variant, playerId, cancellationToken);
        
        _logger.LogInformation(
            "Player left queue: PlayerId={PlayerId}, Variant={Variant}",
            playerId, variant);
        
        return LeaveQueueResult.LeftSuccessfully;
    }

    /// <summary>
    /// Gets the current match status for a player.
    /// First checks Postgres for latest match (source of truth for "in match").
    /// If no active match found, checks Redis queue store (source of truth for "in queue").
    /// Returns null if player is idle (no active match and not queued).
    /// Uses the configured default variant.
    /// </summary>
    public async Task<PlayerMatchStatus?> GetStatusAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        return await GetStatusAsync(playerId, _options.DefaultVariant, cancellationToken);
    }

    /// <summary>
    /// Gets the current match status for a player for a specific variant.
    /// First checks Postgres for latest match (source of truth for "in match").
    /// If no active match found, checks Redis queue store (source of truth for "in queue").
    /// Returns null if player is idle (no active match and not queued).
    /// </summary>
    public async Task<PlayerMatchStatus?> GetStatusAsync(Guid playerId, string variant, CancellationToken cancellationToken = default)
    {
        // First check Postgres for latest match (source of truth for "in match")
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

        // No active match in Postgres - check Redis queue store (source of truth for "in queue")
        var isQueued = await _queueStore.IsQueuedAsync(variant, playerId, cancellationToken);
        
        if (isQueued)
        {
            return new PlayerMatchStatus
            {
                State = PlayerMatchState.Searching,
                MatchId = null,
                BattleId = null,
                Variant = variant,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
        }

        // Not matched and not searching (Idle)
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

