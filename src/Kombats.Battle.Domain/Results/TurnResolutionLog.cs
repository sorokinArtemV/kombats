namespace Kombats.Battle.Domain.Results;

/// <summary>
/// Complete resolution log for a turn, containing both attack directions.
/// </summary>
public sealed record TurnResolutionLog
{
    public Guid BattleId { get; init; }
    public int TurnIndex { get; init; }
    public AttackResolution AtoB { get; init; } = null!;
    public AttackResolution BtoA { get; init; } = null!;
}

