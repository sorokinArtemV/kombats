namespace Kombats.Matchmaking.Domain;

/// <summary>
/// Domain model for a match.
/// </summary>
public class Match
{
    public required Guid MatchId { get; init; }
    public required Guid BattleId { get; init; }
    public required Guid PlayerAId { get; init; }
    public required Guid PlayerBId { get; init; }
    public required string Variant { get; init; }
    public required MatchState State { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required DateTimeOffset UpdatedAtUtc { get; init; }
}




