using Kombats.Matchmaking.Domain;

namespace Kombats.Matchmaking.Application.Abstractions;

/// <summary>
/// Port for player match status read/write operations.
/// </summary>
public interface IPlayerMatchStatusStore
{
    /// <summary>
    /// Gets the current match status for a player.
    /// Returns null if player has no status (not queued and not matched).
    /// </summary>
    Task<PlayerMatchStatus?> GetStatusAsync(Guid playerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the player status to Searching (in queue).
    /// </summary>
    Task SetSearchingAsync(string variant, Guid playerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the player status to Matched with match and battle IDs.
    /// </summary>
    Task SetMatchedAsync(Guid playerId, Guid matchId, Guid battleId, string variant, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the player status (e.g., after leaving queue).
    /// </summary>
    Task RemoveStatusAsync(Guid playerId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Player match status enumeration.
/// </summary>
public enum PlayerMatchState
{
    Searching = 0,
    Matched = 1
}

/// <summary>
/// Player match status model.
/// </summary>
public class PlayerMatchStatus
{
    public required PlayerMatchState State { get; init; }
    public Guid? MatchId { get; init; }
    public Guid? BattleId { get; init; }
    public required string Variant { get; init; }
    public required DateTimeOffset UpdatedAtUtc { get; init; }
    public MatchState? MatchState { get; init; }
}

