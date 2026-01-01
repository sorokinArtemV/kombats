using System.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Combats.Contracts.Battle;
using Combats.Services.Battle.Data;
using Combats.Services.Battle.Data.Entities;

namespace Combats.Services.Battle.Consumers;

public class CreateBattleConsumer : IConsumer<CreateBattle>
{
    private readonly BattleDbContext _dbContext;
    private readonly ILogger<CreateBattleConsumer> _logger;

    public CreateBattleConsumer(
        BattleDbContext dbContext,
        ILogger<CreateBattleConsumer> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<CreateBattle> context)
    {
        var command = context.Message;
        _logger.LogInformation(
            "Processing CreateBattle command for BattleId: {BattleId}, MatchId: {MatchId}",
            command.BattleId, command.MatchId);

        // Create battle entity
        var battle = new BattleEntity
        {
            BattleId = command.BattleId,
            MatchId = command.MatchId,
            PlayerAId = command.PlayerAId,
            PlayerBId = command.PlayerBId,
            State = "ArenaOpen",
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Battles.Add(battle);

        try
        {
            // Save changes first - this will throw on unique violation if battle already exists
            await _dbContext.SaveChangesAsync(context.CancellationToken);

            // Only publish event after successful insert
            var battleCreated = new BattleCreated
            {
                BattleId = battle.BattleId,
                MatchId = battle.MatchId,
                PlayerAId = battle.PlayerAId,
                PlayerBId = battle.PlayerBId,
                State = battle.State,
                BattleServer = null, // Removed hardcoded value
                CreatedAt = battle.CreatedAt,
                Version = 1
            };

            // Publish via ConsumeContext to ensure outbox integration
            await context.Publish(battleCreated, context.CancellationToken);

            // Save changes again for outbox
            await _dbContext.SaveChangesAsync(context.CancellationToken);

            _logger.LogInformation(
                "Successfully created battle {BattleId} and published BattleCreated event",
                command.BattleId);
        }
        catch (DbUpdateException dbEx) when (IsUniqueViolation(dbEx))
        {
            // Battle already exists - idempotent duplicate
            _logger.LogInformation(
                "Battle {BattleId} already exists (unique violation), skipping creation (idempotent behavior)",
                command.BattleId);
            // ACK without publishing duplicate events
            return;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        // PostgreSQL unique violation error code
        return ex.InnerException?.Message?.Contains("23505") == true ||
               ex.InnerException?.Message?.Contains("duplicate key") == true ||
               ex.InnerException?.Message?.Contains("unique constraint") == true;
    }
}

