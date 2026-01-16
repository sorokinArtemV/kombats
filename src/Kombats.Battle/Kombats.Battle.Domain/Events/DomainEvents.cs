using Kombats.Battle.Domain.Model;
using Kombats.Battle.Domain.Results;

namespace Kombats.Battle.Domain.Events;

/// <summary>
/// Domain event: A turn has been resolved with actions from both players.
/// </summary>
public sealed record TurnResolvedDomainEvent(
    Guid BattleId,
    int TurnIndex,
    PlayerAction PlayerAAction,
    PlayerAction PlayerBAction,
    TurnResolutionLog Log,
    DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>
/// Domain event: A player has taken damage.
/// </summary>
public sealed record PlayerDamagedDomainEvent(
    Guid BattleId,
    Guid PlayerId,
    int Damage,
    int RemainingHp,
    int TurnIndex,
    DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>
/// Domain event: The battle has ended.
/// </summary>
public sealed record BattleEndedDomainEvent(
    Guid BattleId,
    Guid? WinnerPlayerId,
    EndBattleReason Reason,
    int FinalTurnIndex,
    DateTimeOffset OccurredAt) : IDomainEvent;


