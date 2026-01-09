namespace Combats.Battle.Application.Abstractions;

/// <summary>
/// Represents a battle turn that has been claimed by a worker for deadline resolution.
/// Contains the battle ID and the turn index that should be resolved.
/// </summary>
public sealed class ClaimedBattleDue
{
    public Guid BattleId { get; init; }
    public int TurnIndex { get; init; }
}

