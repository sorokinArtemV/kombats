using Kombats.Battle.Domain.Rules;

namespace Kombats.Battle.Domain.Results;

/// <summary>
/// Detailed resolution of a single attack from attacker to defender.
/// </summary>
public sealed record AttackResolution
{
    public Guid AttackerId { get; init; }
    public Guid DefenderId { get; init; }
    public int TurnIndex { get; init; }
    public BattleZone? AttackZone { get; init; }
    public BattleZone? DefenderBlockPrimary { get; init; }
    public BattleZone? DefenderBlockSecondary { get; init; }
    public bool WasBlocked { get; init; }
    public bool WasCrit { get; init; }
    public AttackOutcome Outcome { get; init; }
    public int Damage { get; init; }
}

