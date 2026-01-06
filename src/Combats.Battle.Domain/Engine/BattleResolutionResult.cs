using Combats.Battle.Domain.Events;
using Combats.Battle.Domain.Model;

namespace Combats.Battle.Domain.Engine;

/// <summary>
/// Result of resolving a turn in the battle engine.
/// Contains the new state and domain events that occurred.
/// </summary>
public sealed record BattleResolutionResult
{
    public BattleDomainState NewState { get; init; } = null!;
    public IReadOnlyList<IDomainEvent> Events { get; init; } = Array.Empty<IDomainEvent>();
}


