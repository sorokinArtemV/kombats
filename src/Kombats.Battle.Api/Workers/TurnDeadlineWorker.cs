using Kombats.Battle.Application.Abstractions;
using Kombats.Battle.Application.UseCases.Turns;
using Microsoft.Extensions.Options;

namespace Kombats.Battle.Api.Workers;

/// <summary>
/// Background worker that checks Redis ZSET (battle:deadlines) for battles with expired deadlines
/// and resolves their turns. Uses claim-based polling with adaptive delays to drain backlogs efficiently.
/// This is the fallback mechanism for turn resolution when deadlines expire.
/// </summary>
public sealed class TurnDeadlineWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TurnDeadlineWorker> _logger;
    private readonly TurnDeadlineWorkerOptions _options;
    
    private int _consecutiveEmptyIterations = 0;

    public TurnDeadlineWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<TurnDeadlineWorker> logger,
        IOptions<TurnDeadlineWorkerOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Turn deadline worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessClaimBasedTickAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in turn deadline worker iteration");
                // On error, wait a bit before retrying to avoid tight error loops
                await Task.Delay(TimeSpan.FromMilliseconds(_options.ErrorDelayMs), stoppingToken);
                _consecutiveEmptyIterations = 0; // Reset backoff on error
            }
        }

        _logger.LogInformation("Turn deadline worker stopped");
    }

    private async Task ProcessClaimBasedTickAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        
        var stateStore = scope.ServiceProvider.GetRequiredService<IBattleStateStore>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var turnAppService = scope.ServiceProvider.GetRequiredService<BattleTurnAppService>();

        // Get current time and claim due battles
        var now = clock.UtcNow;
        var claimedBattles = await stateStore.ClaimDueBattlesAsync(now, _options.BatchSize, _options.ClaimLeaseTtl, cancellationToken);
        
        // Capture consecutive empty iterations before potential reset (for logging)
        var emptyIterationsBefore = _consecutiveEmptyIterations;
        
        int delayMs;
        if (claimedBattles.Count == 0)
        {
            // No battles claimed - use adaptive backoff
            _consecutiveEmptyIterations++;
            delayMs = Math.Min(
                (int)(_options.IdleDelayMinMs * Math.Pow(2, Math.Min(_consecutiveEmptyIterations - 1, _options.MaxBackoffSteps))),
                _options.IdleDelayMaxMs);
        }
        else
        {
            // Battles claimed - process them and use small delay to drain backlog
            _consecutiveEmptyIterations = 0; // Reset backoff
            delayMs = _options.BacklogDelayMs;
        }

        // Process claimed battles (if any) and log summary with actual delay
        if (claimedBattles.Count > 0)
        {
            await ProcessClaimedBattlesAsync(turnAppService, claimedBattles, delayMs, emptyIterationsBefore, cancellationToken);
        }

        // Delay before next iteration
        await Task.Delay(TimeSpan.FromMilliseconds(delayMs), cancellationToken);
    }

    private async Task ProcessClaimedBattlesAsync(
        BattleTurnAppService turnAppService,
        IReadOnlyList<ClaimedBattleDue> claimedBattles,
        int delayMs,
        int consecutiveEmptyIterationsBefore,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Processing {Count} claimed battles for deadline resolution",
            claimedBattles.Count);

        var resolvedCount = 0;
        var skippedCount = 0;
        var transientErrorCount = 0;

        foreach (var claimed in claimedBattles)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var battleId = claimed.BattleId;
            var claimedTurnIndex = claimed.TurnIndex;

            try
            {
                // ResolveTurnAsync is the single source of truth - it handles all state validation
                // and uses CAS to ensure idempotency. No need to pre-read state.
                var resolved = await turnAppService.ResolveTurnAsync(battleId, cancellationToken);
                
                if (resolved)
                {
                    resolvedCount++;
                    _logger.LogInformation(
                        "Resolved battle claimed by deadline. BattleId: {BattleId}, ClaimedTurnIndex: {ClaimedTurnIndex}",
                        battleId, claimedTurnIndex);
                }
                else
                {
                    // Already resolved / not eligible / concurrent resolution
                    skippedCount++;
                    _logger.LogDebug(
                        "Skipped resolving claimed battle (already resolved / not eligible / concurrent resolution). BattleId: {BattleId}, ClaimedTurnIndex: {ClaimedTurnIndex}",
                        battleId, claimedTurnIndex);
                }
            }
            catch (Exception ex)
            {
                // Transient error (network, Redis, etc.)
                // Do NOT re-add to ZSET - ClaimDueBattlesAsync already postponed the deadline.
                // If worker crashes, the lease will expire and battle will become due again.
                transientErrorCount++;
                _logger.LogWarning(ex,
                    "Transient error processing battle {BattleId} claimed turn {ClaimedTurnIndex}. Battle will be retried after lease expires.",
                    battleId, claimedTurnIndex);
            }
        }

        // Log summary per iteration with actual delay used
        if (transientErrorCount >= _options.TransientErrorWarnThreshold)
        {
            _logger.LogWarning(
                "Processed {Total} claimed battles: {Resolved} resolved, {Skipped} skipped (not resolved), {TransientErrors} transient errors. DelayMs: {DelayMs}, ConsecutiveEmptyIterations: {ConsecutiveEmptyIterations}",
                claimedBattles.Count, resolvedCount, skippedCount, transientErrorCount, delayMs, consecutiveEmptyIterationsBefore);
        }
        else
        {
            _logger.LogDebug(
                "Processed {Total} claimed battles: {Resolved} resolved, {Skipped} skipped (not resolved), {TransientErrors} transient errors. DelayMs: {DelayMs}, ConsecutiveEmptyIterations: {ConsecutiveEmptyIterations}",
                claimedBattles.Count, resolvedCount, skippedCount, transientErrorCount, delayMs, consecutiveEmptyIterationsBefore);
        }
    }

}


