namespace Kombats.Matchmaking.Application.Abstractions;

/// <summary>
/// Port for queue operations (atomic join/leave/pop pairs).
/// </summary>
public interface IMatchQueueStore
{
    /// <summary>
    /// Adds a player to the queue atomically (idempotent if already queued).
    /// Returns true if the player was added, false if already in queue.
    /// </summary>
    Task<bool> TryJoinQueueAsync(string variant, Guid playerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a player from the queue atomically (idempotent if not in queue).
    /// Returns true if the player was removed, false if not in queue.
    /// </summary>
    Task<bool> TryLeaveQueueAsync(string variant, Guid playerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically pops a pair of players from the queue.
    /// Returns the pair if both players are available, null otherwise.
    /// </summary>
    Task<(Guid PlayerAId, Guid PlayerBId)?> TryPopPairAsync(string variant, CancellationToken cancellationToken = default);
}




