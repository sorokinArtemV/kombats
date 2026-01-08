using Combats.Battle.Application.Abstractions;
using Combats.Battle.Application.Policies.Time;
using Combats.Battle.Application.Services;
using Combats.Battle.Domain.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Combats.Battle.Api.Workers;

/// <summary>
/// Background worker that periodically checks Redis ZSET (battle:deadlines) for battles with expired deadlines
/// and resolves their turns. This is the fallback mechanism for turn resolution when deadlines expire.
/// </summary>
public sealed class TurnDeadlineWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TurnDeadlineWorker> _logger;

    private const int PollIntervalMs = 1000; // Poll every 300ms
    private const int BatchSize = 50; // Process up to 50 battles per iteration
    private const int SkewMs = 100; // Small buffer for clock skew (ms)

    public TurnDeadlineWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<TurnDeadlineWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Turn deadline worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDueBattlesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in turn deadline worker iteration");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(PollIntervalMs), stoppingToken);
        }

        _logger.LogInformation("Turn deadline worker stopped");
    }

    private async Task ProcessDueBattlesAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        
        var stateStore = scope.ServiceProvider.GetRequiredService<IBattleStateStore>();
        var turnAppService = scope.ServiceProvider.GetRequiredService<BattleTurnAppService>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var now = clock.UtcNow;
        
        // Get battles with expired deadlines
        var dueBattles = await stateStore.GetDueBattlesAsync(now, BatchSize, cancellationToken);
        
        if (dueBattles.Count == 0)
            return;

        _logger.LogDebug(
            "Found {Count} battles with expired deadlines",
            dueBattles.Count);

        var resolvedCount = 0;
        var skippedCount = 0;

        foreach (var battleId in dueBattles)
        {
            try
            {
                // Load state to get current turn index
                var state = await stateStore.GetStateAsync(battleId, cancellationToken);
                if (state == null)
                {
                    // Battle doesn't exist - remove from deadlines
                    await stateStore.RemoveBattleDeadlineAsync(battleId, cancellationToken);
                    continue;
                }

                // Only process battles in TurnOpen phase
                if (state.Phase != BattlePhase.TurnOpen)
                {
                    // Battle is not in TurnOpen - remove from deadlines if it's ended
                    if (state.Phase == BattlePhase.Ended)
                    {
                        await stateStore.RemoveBattleDeadlineAsync(battleId, cancellationToken);
                    }
                    skippedCount++;
                    continue;
                }

                var turnIndex = state.TurnIndex;
                var deadlineUtc = state.DeadlineUtc;
                
				// Check if deadline has actually passed (with small buffer for clock skew)
				// Only resolve when now is after deadline (plus small skew buffer)
				if (!TurnDeadlinePolicy.ShouldResolve(now, deadlineUtc, SkewMs))
                {
                    // Deadline hasn't passed yet (clock skew or race condition)
                    skippedCount++;
                    continue;
                }

                // Try to resolve the turn
                // BattleTurnAppService will use CAS to ensure only one resolution happens
                var resolved = await turnAppService.ResolveTurnAsync(battleId, cancellationToken);
                
                if (resolved)
                {
                    resolvedCount++;
                    
                    // Log timing information
                    var lateness = (now - deadlineUtc).TotalMilliseconds;
                    if (lateness > 100)
                    {
                        _logger.LogInformation(
                            "Resolved turn {TurnIndex} for BattleId: {BattleId} with {LatenessMs:F0}ms lateness",
                            turnIndex, battleId, lateness);
                    }
                }
                else
                {
                    skippedCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error processing battle {BattleId} in deadline worker",
                    battleId);
            }
        }

        if (resolvedCount > 0 || skippedCount > 0)
        {
                _logger.LogDebug(
                "Processed {Total} battles: {Resolved} resolved, {Skipped} skipped",
                dueBattles.Count, resolvedCount, skippedCount);
        }
    }

}


