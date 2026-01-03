using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Combats.Services.Battle.State;
using Combats.Services.Battle.Constants;
using MassTransit;
using Combats.Contracts.Battle;

namespace Combats.Services.Battle.Services;

/// <summary>
/// Watchdog service that periodically scans active battles and recovers missing/overdue ResolveTurn schedules.
/// This prevents battles from stalling if ScheduleSend fails due to transient RabbitMQ issues.
///
/// Anti-stall mechanism (Option A):
/// - Every ScanIntervalSeconds, scans all active battles
/// - For each battle in TurnOpen phase:
///   - Checks if NextResolveScheduledUtcTicks is set and recent (within grace period)
///   - If missing or overdue (deadline passed but no scheduled message), reschedules ResolveTurn
/// - Idempotent: only schedules if not already scheduled or if scheduled time is stale
/// </summary>
public sealed class BattleWatchdogService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BattleWatchdogService> _logger;

    private const int ScanIntervalSeconds = 5; // Scan every 5 seconds
    private const int GracePeriodSeconds = 2;  // Allow 2 seconds grace period before considering overdue

    public BattleWatchdogService(
        IServiceScopeFactory scopeFactory,
        ILogger<BattleWatchdogService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Battle watchdog service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanAndRecoverAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in battle watchdog scan");
            }

            await Task.Delay(TimeSpan.FromSeconds(ScanIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("Battle watchdog service stopped");
    }

    private async Task ScanAndRecoverAsync(CancellationToken cancellationToken)
    {
        // Create a scope for this scan iteration so we can resolve scoped services safely.
        using var scope = _scopeFactory.CreateScope();

        var stateStore = scope.ServiceProvider.GetRequiredService<IBattleStateStore>();
        var messageScheduler = scope.ServiceProvider.GetRequiredService<IMessageScheduler>();

        var activeBattles = await stateStore.GetActiveBattlesAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var recoveredCount = 0;

        foreach (var battleId in activeBattles)
        {
            try
            {
                var state = await stateStore.GetStateAsync(battleId, cancellationToken);
                if (state == null)
                    continue;

                // Only check battles in TurnOpen phase
                if (state.Phase != BattlePhase.TurnOpen)
                    continue;

                var deadlineUtc = state.GetDeadlineUtc();
                var scheduledUtc = state.GetNextResolveScheduledUtc();
                var needsReschedule = false;

                // Check if ResolveTurn needs to be scheduled or rescheduled
                if (scheduledUtc == null)
                {
                    // Not scheduled at all - needs scheduling
                    needsReschedule = true;
                    _logger.LogWarning(
                        "Battle {BattleId}, Turn {TurnIndex} has no scheduled ResolveTurn. Recovering...",
                        battleId, state.TurnIndex);
                }
                else if (now > deadlineUtc.AddSeconds(GracePeriodSeconds) &&
                         now > scheduledUtc.Value.AddSeconds(GracePeriodSeconds))
                {
                    // Deadline passed and scheduled time is stale - reschedule
                    needsReschedule = true;
                    _logger.LogWarning(
                        "Battle {BattleId}, Turn {TurnIndex} has overdue ResolveTurn (deadline: {DeadlineUtc}, scheduled: {ScheduledUtc}). Recovering...",
                        battleId, state.TurnIndex, deadlineUtc, scheduledUtc.Value);
                }

                if (!needsReschedule)
                    continue;

                // Reschedule ResolveTurn at the deadline (or now if deadline passed)
                var scheduleAt = deadlineUtc > now ? deadlineUtc : now;
                var resolveTurnCommand = new ResolveTurn(battleId, state.TurnIndex);

                try
                {
                    // Schedule using message scheduler
                    // Note: IMessageScheduler.ScheduleSend doesn't support pipe configuration.
                    // MassTransit auto-generates MessageId. CorrelationId should be battleId for observability.
                    await messageScheduler.ScheduleSend(
                        new Uri($"queue:{BattleQueues.ResolveTurn}"),
                        scheduleAt,
                        resolveTurnCommand,
                        cancellationToken);

                    // Mark as scheduled
                    await stateStore.MarkResolveScheduledAsync(battleId, scheduleAt, cancellationToken);

                    recoveredCount++;
                    _logger.LogInformation(
                        "Recovered ResolveTurn schedule for BattleId: {BattleId}, TurnIndex: {TurnIndex}, ScheduledAt: {ScheduledAt}",
                        battleId, state.TurnIndex, scheduleAt);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to reschedule ResolveTurn for BattleId: {BattleId}, TurnIndex: {TurnIndex}",
                        battleId, state.TurnIndex);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing battle {BattleId} in watchdog scan", battleId);
            }
        }

        if (recoveredCount > 0)
        {
            _logger.LogInformation("Watchdog recovered {Count} missing/overdue ResolveTurn schedules", recoveredCount);
        }
    }
}
