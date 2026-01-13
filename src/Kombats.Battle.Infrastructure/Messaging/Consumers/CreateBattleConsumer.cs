using System.Data;
using Kombats.Battle.Application.UseCases.Lifecycle;
using Kombats.Battle.Infrastructure.Persistence.EF;
using Kombats.Contracts.Battle;
using Kombats.Battle.Infrastructure.Persistence.EF.DbContext;
using Kombats.Battle.Infrastructure.Persistence.EF.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kombats.Battle.Infrastructure.Messaging.Consumers;

/// <summary>
/// Consumer for CreateBattle command.
/// Creates battle entity in DB, publishes BattleCreated event, and initializes battle state in Redis.
/// </summary>
public class CreateBattleConsumer : IConsumer<CreateBattle>
{
    private readonly BattleDbContext _dbContext;
    private readonly BattleLifecycleAppService _lifecycleService;
    private readonly ILogger<CreateBattleConsumer> _logger;

    public CreateBattleConsumer(
        BattleDbContext dbContext,
        BattleLifecycleAppService lifecycleService,
        ILogger<CreateBattleConsumer> logger)
    {
        _dbContext = dbContext;
        _lifecycleService = lifecycleService;
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

            // Initialize battle state in Redis (selects ruleset from config, generates seed)
            var initResult = await _lifecycleService.HandleBattleCreatedAsync(
                battle.BattleId,
                battle.MatchId,
                battle.PlayerAId,
                battle.PlayerBId,
                context.CancellationToken);

            if (initResult == null)
            {
                // Initialization failed (non-retryable error - already logged)
                // Rollback DB transaction by not saving changes
                _logger.LogWarning(
                    "Battle initialization failed for BattleId: {BattleId}. Not publishing BattleCreated event.",
                    command.BattleId);
                return;
            }

            // Publish event after successful initialization (includes ruleset version and seed)
            var battleCreated = new BattleCreated
            {
                BattleId = battle.BattleId,
                MatchId = battle.MatchId,
                PlayerAId = battle.PlayerAId,
                PlayerBId = battle.PlayerBId,
                RulesetVersion = initResult.RulesetVersion,
                Seed = initResult.Seed,
                State = battle.State,
                BattleServer = null,
                CreatedAt = battle.CreatedAt,
                Version = 1
            };

            // Publish via ConsumeContext to ensure outbox integration
            await context.Publish(battleCreated, context.CancellationToken);

            // Save changes again for outbox
            await _dbContext.SaveChangesAsync(context.CancellationToken);

            _logger.LogInformation(
                "Successfully created battle {BattleId}, published BattleCreated event, and initialized Redis state",
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


