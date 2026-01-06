using Combats.Contracts.Battle;

namespace Combats.Battle.Application.Ports;

/// <summary>
/// Port interface for publishing integration events.
/// Application defines what it needs; Infrastructure provides MassTransit implementation.
/// </summary>
public interface IBattleEventPublisher
{
    Task PublishBattleEndedAsync(
        Guid battleId,
        Guid matchId,
        BattleEndReason reason,
        Guid? winnerPlayerId,
        DateTime endedAt,
        CancellationToken cancellationToken = default);
}



