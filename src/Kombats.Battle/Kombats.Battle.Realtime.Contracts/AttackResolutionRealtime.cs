namespace Combats.Battle.Realtime.Contracts;

/// <summary>
/// Realtime contract for attack resolution.
/// Does not expose RNG roll numbers or chance values.
/// </summary>
public record AttackResolutionRealtime
{
    public Guid AttackerId { get; init; }
    public Guid DefenderId { get; init; }
    public int TurnIndex { get; init; }
    public string? AttackZone { get; init; }
    public string? DefenderBlockPrimary { get; init; }
    public string? DefenderBlockSecondary { get; init; }
    public bool WasBlocked { get; init; }
    public bool WasCrit { get; init; }
    public AttackOutcomeRealtime Outcome { get; init; }
    public int Damage { get; init; }
}

