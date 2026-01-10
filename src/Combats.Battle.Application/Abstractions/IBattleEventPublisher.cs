using Combats.Battle.Domain.Results;

namespace Combats.Battle.Application.Abstractions;

/// <summary>
/// Port interface for publishing integration events.
/// Application defines what it needs; Infrastructure provides MassTransit implementation.
/// </summary>
public interface IBattleEventPublisher
{
    Task PublishBattleEndedAsync(
        Guid battleId,
        Guid matchId,
        EndBattleReason reason,
        Guid? winnerPlayerId,
        DateTime endedAt,
        CancellationToken cancellationToken = default);
}





