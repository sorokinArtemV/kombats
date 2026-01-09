using Combats.Battle.Application.Abstractions;
using Combats.Battle.Application.UseCases.Turns;
using Combats.Battle.Domain.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Combats.Battle.Api.Workers;

/// <summary>
/// Background worker that checks Redis ZSET (battle:deadlines) for battles with expired deadlines
/// and resolves their turns. Uses sleep-until-next-deadline approach to reduce unnecessary polling.
/// This is the fallback mechanism for turn resolution when deadlines expire.
/// </summary>
public sealed class TurnDeadlineWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TurnDeadlineWorker> _logger;

    private const int DefaultIdleIntervalMs = 500; // Default delay when no deadlines exist
    private const int BatchSize = 50; // Process up to 50 battles per iteration
    private const int SkewMs = 100; // Small buffer for clock skew (ms)
    private const int MinDelayMs = 50; // Minimum delay to avoid tight loops
    private const int MaxDelayMs = 1000; // Maximum delay cap

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
                await ProcessDueBattlesWithSleepAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in turn deadline worker iteration");
                // On error, wait a bit before retrying to avoid tight error loops
                await Task.Delay(TimeSpan.FromMilliseconds(DefaultIdleIntervalMs), stoppingToken);
            }
        }

        _logger.LogInformation("Turn deadline worker stopped");
    }

    private async Task ProcessDueBattlesWithSleepAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        
        var stateStore = scope.ServiceProvider.GetRequiredService<IBattleStateStore>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var now = clock.UtcNow;
        
        // Get the next upcoming deadline
        var nextDeadline = await stateStore.GetNextDeadlineUtcAsync(cancellationToken);
        
        int delayMs;
        if (nextDeadline == null)
        {
            // No active deadlines - use default idle interval
            delayMs = DefaultIdleIntervalMs;
        }
        else
        {
            // Compute delay until next deadline (with small skew buffer)
            var timeUntilDeadline = (nextDeadline.Value - now).TotalMilliseconds - SkewMs;
            
            // Clamp delay between min and max
            delayMs = (int)Math.Clamp(timeUntilDeadline, MinDelayMs, MaxDelayMs);
        }

        // Sleep until next deadline (or default interval)
        await Task.Delay(TimeSpan.FromMilliseconds(delayMs), cancellationToken);

        // After sleep, check for due battles
        now = clock.UtcNow; // Re-read time after sleep
        var dueBattles = await stateStore.GetDueBattlesAsync(now, BatchSize, cancellationToken);
        
        if (dueBattles.Count == 0)
            return;

        // Process due battles
        await ProcessDueBattlesAsync(scope, stateStore, dueBattles, now, cancellationToken);
    }

    private async Task ProcessDueBattlesAsync(
        IServiceScope scope,
        IBattleStateStore stateStore,
        List<Guid> dueBattles,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var turnAppService = scope.ServiceProvider.GetRequiredService<BattleTurnAppService>();

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
                    
                    // Log timing information only for meaningful lateness
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


