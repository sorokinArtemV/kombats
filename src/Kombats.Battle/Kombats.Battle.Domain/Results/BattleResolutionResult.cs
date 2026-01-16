using Kombats.Battle.Domain.Events;
using Kombats.Battle.Domain.Model;

namespace Kombats.Battle.Domain.Results;

/// <summary>
/// Result of resolving a turn in the battle engine.
/// Contains the new state and domain events that occurred.
/// </summary>
public sealed record BattleResolutionResult
{
    public BattleDomainState NewState { get; init; } = null!;
    public IReadOnlyList<IDomainEvent> Events { get; init; } = Array.Empty<IDomainEvent>();
}


