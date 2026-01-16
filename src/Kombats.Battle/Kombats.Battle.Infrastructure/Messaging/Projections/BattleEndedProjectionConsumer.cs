using Kombats.Battle.Infrastructure.Data.DbContext;
using Kombats.Battle.Contracts.Battle;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kombats.Battle.Infrastructure.Messaging.Projections;

/// <summary>
/// Projection consumer for BattleEnded events.
/// Updates the read model (Postgres Battle entity) when a battle ends.
/// </summary>
public class BattleEndedProjectionConsumer : IConsumer<BattleEnded>
{
    private readonly BattleDbContext _dbContext;
    private readonly ILogger<BattleEndedProjectionConsumer> _logger;

    public BattleEndedProjectionConsumer(
        BattleDbContext dbContext,
        ILogger<BattleEndedProjectionConsumer> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<BattleEnded> context)
    {
        var battleEnded = context.Message;
        var battleId = battleEnded.BattleId;

        _logger.LogInformation(
            "Processing BattleEnded projection for BattleId: {BattleId}, Reason: {Reason}, MessageId: {MessageId}",
            battleId, battleEnded.Reason, context.MessageId);

        // Load battle by BattleId (PK)
        var battle = await _dbContext.Battles
            .FirstOrDefaultAsync(b => b.BattleId == battleId, context.CancellationToken);

        if (battle == null)
        {
            _logger.LogWarning(
                "Battle {BattleId} not found in read model for BattleEnded projection. " +
                "This is idempotent - battle may have been created only in Redis. MessageId: {MessageId}", battleId,
                context.MessageId);
            return;
        }

        // Idempotency check: if battle is already in terminal state, treat as idempotent
        if (battle.State == "Ended" && battle.EndedAt != null)
        {
            // Verify that the existing data matches the event (for consistency check)
            var existingEndReason = battle.EndReason;
            var existingWinnerPlayerId = battle.WinnerPlayerId;
            var existingEndedAt = battle.EndedAt;

            // If all fields match, this is a duplicate event - idempotent, just log
            if (existingEndReason == battleEnded.Reason.ToString() &&
                existingWinnerPlayerId == battleEnded.WinnerPlayerId &&
                existingEndedAt == battleEnded.EndedAt)
            {
                _logger.LogInformation(
                    "Battle {BattleId} already ended with matching data. Duplicate BattleEnded event (idempotent). " +
                    "Reason: {Reason}, WinnerPlayerId: {WinnerPlayerId}, EndedAt: {EndedAt}, MessageId: {MessageId}",
                    battleId, battleEnded.Reason, battleEnded.WinnerPlayerId, battleEnded.EndedAt, context.MessageId);
                return;
            }

            // If fields don't match, log warning but don't overwrite (first write wins for read model)
            _logger.LogWarning(
                "Battle {BattleId} already ended but with different data. Existing: Reason={ExistingReason}, " +
                "WinnerPlayerId={ExistingWinner}, EndedAt={ExistingEndedAt}. " +
                "Event: Reason={EventReason}, WinnerPlayerId={EventWinner}, EndedAt={EventEndedAt}. " +
                "Keeping existing data (first write wins). MessageId: {MessageId}",
                battleId, existingEndReason, existingWinnerPlayerId, existingEndedAt,
                battleEnded.Reason, battleEnded.WinnerPlayerId, battleEnded.EndedAt, context.MessageId);
            return;
        }

        // Update read model with event data
        battle.State = "Ended";
        battle.EndedAt = battleEnded.EndedAt;
        battle.EndReason = battleEnded.Reason.ToString();
        battle.WinnerPlayerId = battleEnded.WinnerPlayerId;

        // Save changes (single SaveChangesAsync call)
        await _dbContext.SaveChangesAsync(context.CancellationToken);

        _logger.LogInformation(
            "Successfully updated read model for Battle {BattleId} from BattleEnded event. " +
            "Reason: {Reason}, WinnerPlayerId: {WinnerPlayerId}, EndedAt: {EndedAt}, MessageId: {MessageId}",
            battleId, battleEnded.Reason, battleEnded.WinnerPlayerId, battleEnded.EndedAt, context.MessageId);
    }
}