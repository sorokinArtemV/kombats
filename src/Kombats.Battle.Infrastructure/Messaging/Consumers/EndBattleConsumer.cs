using Kombats.Battle.Infrastructure.Persistence.EF;
using Kombats.Contracts.Battle;
using Kombats.Battle.Infrastructure.Persistence.EF.DbContext;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kombats.Battle.Infrastructure.Messaging.Consumers;

/// <summary>
/// Consumer for EndBattle command.
/// This consumer validates the request and publishes a BattleEnded event.
/// </summary>
public class EndBattleConsumer : IConsumer<EndBattle>
{
    private readonly BattleDbContext _dbContext;
    private readonly ILogger<EndBattleConsumer> _logger;

    public EndBattleConsumer(
        BattleDbContext dbContext,
        ILogger<EndBattleConsumer> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<EndBattle> context)
    {
        var command = context.Message;
        _logger.LogInformation(
            "Processing EndBattle command for BattleId: {BattleId}, Reason: {Reason}, MessageId: {MessageId}",
            command.BattleId, command.Reason, context.MessageId);

        // Load battle by BattleId (PK) for validation only
        var battle = await _dbContext.Battles
            .FirstOrDefaultAsync(b => b.BattleId == command.BattleId, context.CancellationToken);

        if (battle == null)
        {
            _logger.LogWarning(
                "Battle {BattleId} not found in read model for EndBattle command. " +
                "This is idempotent - battle may exist only in Redis. MessageId: {MessageId}",
                command.BattleId, context.MessageId);
            // ACK without side effects (battle may exist in Redis but not in DB read model)
            return;
        }

        // Idempotency check: if battle already ended, do not publish duplicate BattleEnded
        if (battle.State == "Ended" && battle.EndedAt != null)
        {
            _logger.LogInformation(
                "Battle {BattleId} already ended, skipping EndBattle command (idempotent behavior). " +
                "Existing: Reason={ExistingReason}, EndedAt={ExistingEndedAt}, MessageId: {MessageId}",
                command.BattleId, battle.EndReason, battle.EndedAt, context.MessageId);
            // ACK without publishing duplicate events
            return;
        }

        // Determine WinnerPlayerId based on reason
        var winnerPlayerId = DetermineWinner(command.Reason);

        // Publish BattleEnded event (canonical termination event)
        // DB update will be handled by BattleEndedProjectionConsumer
        var battleEnded = new BattleEnded
        {
            BattleId = command.BattleId,
            MatchId = command.MatchId,
            Reason = command.Reason,
            WinnerPlayerId = winnerPlayerId,
            EndedAt = DateTime.UtcNow, // Use current time as authoritative ended timestamp
            Version = 1
        };

        // Publish via ConsumeContext to ensure outbox integration
        await context.Publish(battleEnded, context.CancellationToken);

        _logger.LogInformation(
            "Published BattleEnded event for BattleId: {BattleId}, Reason: {Reason}, WinnerPlayerId: {WinnerPlayerId}. " +
            "DB update will be handled by BattleEndedProjectionConsumer. MessageId: {MessageId}",
            command.BattleId, command.Reason, winnerPlayerId, context.MessageId);
    }

    private static Guid? DetermineWinner(BattleEndReason reason)
    {
        // For v1: only Normal/AdminForced might have winners, but we don't compute them yet
        // For DoubleForfeit, Cancelled, SystemError, Timeout -> null
        return null;
    }
}


