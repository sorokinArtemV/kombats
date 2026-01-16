namespace Kombats.Contracts.Battle;

/// <summary>
/// Event published when a battle is created.
/// </summary>
public record BattleCreated
{
    public Guid BattleId { get; init; }
    public Guid MatchId { get; init; }
    public Guid PlayerAId { get; init; }
    public Guid PlayerBId { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
}





