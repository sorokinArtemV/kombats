using System.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Combats.Contracts.Battle;
using Combats.Services.Battle.Data;

namespace Combats.Services.Battle.Consumers;

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
            "Processing EndBattle command for BattleId: {BattleId}, Reason: {Reason}",
            command.BattleId, command.Reason);

        // Load battle by BattleId (PK)
        var battle = await _dbContext.Battles
            .FirstOrDefaultAsync(b => b.BattleId == command.BattleId, context.CancellationToken);

        if (battle == null)
        {
            _logger.LogWarning(
                "Battle {BattleId} not found, skipping EndBattle (idempotent behavior)",
                command.BattleId);
            // ACK without side effects
            return;
        }

        // Idempotency check: if battle already ended, do not process again
        if (battle.State == "Ended" || battle.EndedAt != null)
        {
            _logger.LogInformation(
                "Battle {BattleId} already ended, skipping EndBattle (idempotent behavior)",
                command.BattleId);
            // ACK without publishing duplicate events
            return;
        }

        // Update battle state
        battle.State = "Ended";
        battle.EndedAt = DateTime.UtcNow;
        battle.EndReason = command.Reason.ToString();

        // Determine WinnerPlayerId based on reason
        battle.WinnerPlayerId = DetermineWinner(command.Reason);

        // Save changes
        await _dbContext.SaveChangesAsync(context.CancellationToken);

        // Publish BattleEnded event
        var battleEnded = new BattleEnded
        {
            BattleId = battle.BattleId,
            MatchId = battle.MatchId,
            Reason = command.Reason,
            WinnerPlayerId = battle.WinnerPlayerId,
            EndedAt = battle.EndedAt.Value,
            Version = 1
        };

        // Publish via ConsumeContext to ensure outbox integration
        await context.Publish(battleEnded, context.CancellationToken);

        // Save changes again for outbox
        await _dbContext.SaveChangesAsync(context.CancellationToken);

        _logger.LogInformation(
            "Successfully ended battle {BattleId} and published BattleEnded event",
            command.BattleId);
    }

    private static Guid? DetermineWinner(BattleEndReason reason)
    {
        // For v1: only Normal/AdminForced might have winners, but we don't compute them yet
        // For DoubleForfeit, Cancelled, SystemError, Timeout -> null
        return null;
    }
}
