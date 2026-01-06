using Combats.Battle.Domain.Model;
using Combats.Contracts.Battle;

namespace Combats.Battle.Domain.Events;

/// <summary>
/// Domain event: A turn has been resolved with actions from both players.
/// </summary>
public sealed record TurnResolvedDomainEvent(
    Guid BattleId,
    int TurnIndex,
    PlayerAction PlayerAAction,
    PlayerAction PlayerBAction,
    DateTime OccurredAt) : IDomainEvent;

/// <summary>
/// Domain event: A player has taken damage.
/// </summary>
public sealed record PlayerDamagedDomainEvent(
    Guid BattleId,
    Guid PlayerId,
    int Damage,
    int RemainingHp,
    int TurnIndex,
    DateTime OccurredAt) : IDomainEvent;

/// <summary>
/// Domain event: The battle has ended.
/// Uses BattleEndReason from Contracts (shared enum).
/// </summary>
public sealed record BattleEndedDomainEvent(
    Guid BattleId,
    Guid? WinnerPlayerId,
    BattleEndReason Reason,
    int FinalTurnIndex,
    DateTime OccurredAt) : IDomainEvent;


