using Combats.Battle.Application.Services;
using Combats.Contracts.Battle;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Combats.Battle.Infrastructure.Consumers;

/// <summary>
/// Thin adapter consumer for BattleCreated event.
/// Delegates to Application service for orchestration.
/// </summary>
public class BattleCreatedEngineConsumer : IConsumer<BattleCreated>
{
    private readonly BattleLifecycleAppService _lifecycleService;
    private readonly ILogger<BattleCreatedEngineConsumer> _logger;

    public BattleCreatedEngineConsumer(
        BattleLifecycleAppService lifecycleService,
        ILogger<BattleCreatedEngineConsumer> logger)
    {
        _lifecycleService = lifecycleService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<BattleCreated> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Processing BattleCreated event for BattleId: {BattleId}, MessageId: {MessageId}, CorrelationId: {CorrelationId}",
            message.BattleId, context.MessageId, context.CorrelationId);

        await _lifecycleService.HandleBattleCreatedAsync(message, context.CancellationToken);
    }
}


