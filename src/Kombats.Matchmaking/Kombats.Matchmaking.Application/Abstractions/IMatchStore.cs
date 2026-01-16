namespace Kombats.Matchmaking.Application.Abstractions;

/// <summary>
/// Port for match record storage operations.
/// </summary>
public interface IMatchStore
{
    /// <summary>
    /// Stores a match record with TTL.
    /// </summary>
    Task StoreMatchAsync(MatchRecord match, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a match record by match ID.
    /// Returns null if not found or expired.
    /// </summary>
    Task<MatchRecord?> GetMatchAsync(Guid matchId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Match record model.
/// </summary>
public class MatchRecord
{
    public required Guid MatchId { get; init; }
    public required Guid BattleId { get; init; }
    public required Guid PlayerAId { get; init; }
    public required Guid PlayerBId { get; init; }
    public required string Variant { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
}




