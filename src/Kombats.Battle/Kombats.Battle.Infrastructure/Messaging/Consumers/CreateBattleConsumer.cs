using Kombats.Battle.Application.UseCases.Lifecycle;
using Kombats.Battle.Infrastructure.Data.DbContext;
using Kombats.Battle.Infrastructure.Data.Entities;
using Kombats.Battle.Contracts.Battle;
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
        _logger.LogInformation("Processing CreateBattle command for BattleId: {BattleId}, MatchId: {MatchId}",
            command.BattleId, command.MatchId);

        var battle = new BattleEntity
        {
            BattleId = command.BattleId,
            MatchId = command.MatchId,
            PlayerAId = command.PlayerAId,
            PlayerBId = command.PlayerBId,
            State = "ArenaOpen",
            CreatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.Battles.Add(battle);

        try
        {
            await _dbContext.SaveChangesAsync(context.CancellationToken);

            var initResult = await _lifecycleService.HandleBattleCreatedAsync(
                battle.BattleId,
                battle.MatchId,
                battle.PlayerAId,
                battle.PlayerBId,
                context.CancellationToken);

            if (initResult == null)
            {
                _logger.LogWarning(
                    "Battle initialization failed for BattleId: {BattleId}. Not publishing BattleCreated event.",
                    command.BattleId);
                return;
            }

            _logger.LogInformation(
                "Successfully created battle {BattleId}, published BattleCreated event, and initialized Redis state",
                command.BattleId);
        }
        catch (DbUpdateException dbEx) when (IsUniqueViolation(dbEx))
        {
            _logger.LogInformation(
                "Battle {BattleId} already exists (unique violation), skipping creation (idempotent behavior)",
                command.BattleId);
            // ACK without publishing duplicate events
            return;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        return ex.InnerException?.Message?.Contains("23505") == true ||
               ex.InnerException?.Message?.Contains("duplicate key") == true ||
               ex.InnerException?.Message?.Contains("unique constraint") == true;
    }
}