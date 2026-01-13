namespace Combats.Battle.Realtime.Contracts;

/// <summary>
/// Realtime contract for turn resolution log.
/// </summary>
public record TurnResolutionLogRealtime
{
    public Guid BattleId { get; init; }
    public int TurnIndex { get; init; }
    public AttackResolutionRealtime AtoB { get; init; } = null!;
    public AttackResolutionRealtime BtoA { get; init; } = null!;
}

