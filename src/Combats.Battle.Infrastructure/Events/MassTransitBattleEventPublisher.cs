using Combats.Battle.Application.Ports;
using Combats.Contracts.Battle;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Combats.Battle.Infrastructure.Events;

/// <summary>
/// MassTransit implementation of IBattleEventPublisher.
/// Publishes integration events via MassTransit (with outbox support).
/// </summary>
public class MassTransitBattleEventPublisher : IBattleEventPublisher
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<MassTransitBattleEventPublisher> _logger;

    public MassTransitBattleEventPublisher(
        IPublishEndpoint publishEndpoint,
        ILogger<MassTransitBattleEventPublisher> logger)
    {
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task PublishBattleEndedAsync(
        Guid battleId,
        Guid matchId,
        BattleEndReason reason,
        Guid? winnerPlayerId,
        DateTime endedAt,
        CancellationToken cancellationToken = default)
    {
        var battleEnded = new BattleEnded
        {
            BattleId = battleId,
            MatchId = matchId,
            Reason = reason,
            WinnerPlayerId = winnerPlayerId,
            EndedAt = endedAt,
            Version = 1
        };

        await _publishEndpoint.Publish(battleEnded, cancellationToken);

        _logger.LogInformation(
            "Published BattleEnded event for BattleId: {BattleId}, Reason: {Reason}, Winner: {WinnerPlayerId}",
            battleId, reason, winnerPlayerId);
    }
}


