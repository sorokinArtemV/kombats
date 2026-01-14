using Kombats.Battle.Application.Abstractions;
using Kombats.Battle.Domain.Results;
using Kombats.Contracts.Battle;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Kombats.Battle.Infrastructure.Messaging.Publisher;

/// <summary>
/// MassTransit implementation of IBattleEventPublisher.
/// Publishes integration events via MassTransit (with outbox support).
/// Maps domain EndBattleReason to Contracts BattleEndReason.
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
        EndBattleReason reason,
        Guid? winnerPlayerId,
        DateTime endedAt,
        CancellationToken cancellationToken = default)
    {
        // Map domain EndBattleReason to Contracts BattleEndReason
        var contractReason = MapReason(reason);

        var battleEnded = new BattleEnded
        {
            BattleId = battleId,
            MatchId = matchId,
            Reason = contractReason,
            WinnerPlayerId = winnerPlayerId,
            EndedAt = endedAt,
            Version = 1
        };

        await _publishEndpoint.Publish(battleEnded, cancellationToken);

        _logger.LogInformation(
            "Published BattleEnded event for BattleId: {BattleId}, Reason: {Reason}, Winner: {WinnerPlayerId}",
            battleId, contractReason, winnerPlayerId);
    }

    private static BattleEndReason MapReason(EndBattleReason domainReason)
    {
        return domainReason switch
        {
            EndBattleReason.Normal => BattleEndReason.Normal,
            EndBattleReason.DoubleForfeit => BattleEndReason.DoubleForfeit,
            EndBattleReason.Timeout => BattleEndReason.Timeout,
            EndBattleReason.Cancelled => BattleEndReason.Cancelled,
            EndBattleReason.AdminForced => BattleEndReason.AdminForced,
            _ => BattleEndReason.SystemError
        };
    }
}


