using Kombats.Battle.Application.Abstractions;
using Kombats.Battle.Application.UseCases.Turns;
using Kombats.Battle.Domain.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

    private const int BatchSize = 50; // Process up to 50 battles per iteration
    private static readonly TimeSpan ClaimLeaseTtl = TimeSpan.FromSeconds(4); // Lock TTL for claim processing (reduced for faster recovery after crashes)
    
    // Adaptive delay configuration
    private const int IdleDelayMinMs = 200; // Base delay when no battles claimed
    private const int IdleDelayMaxMs = 1000; // Maximum delay when no battles claimed
    private const int BacklogDelayMs = 30; // Small delay when backlog exists (to drain efficiently)
    
    private int _consecutiveEmptyIterations = 0; // For backoff when idle

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
                await ProcessClaimBasedTickAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in turn deadline worker iteration");
                // On error, wait a bit before retrying to avoid tight error loops
                await Task.Delay(TimeSpan.FromMilliseconds(IdleDelayMinMs), stoppingToken);
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

        // Get current time and claim due battles
        var now = clock.UtcNow;
        var claimedBattles = await stateStore.ClaimDueBattlesAsync(now, BatchSize, ClaimLeaseTtl, cancellationToken);
        
        int delayMs;
        if (claimedBattles.Count == 0)
        {
            // No battles claimed - use adaptive backoff: 200ms, 400ms, 800ms, max 1000ms
            _consecutiveEmptyIterations++;
            delayMs = Math.Min(
                (int)(IdleDelayMinMs * Math.Pow(2, Math.Min(_consecutiveEmptyIterations - 1, 3))),
                IdleDelayMaxMs);
        }
        else
        {
            // Battles claimed - process them and use small delay to drain backlog
            _consecutiveEmptyIterations = 0; // Reset backoff
            await ProcessClaimedBattlesAsync(scope, stateStore, claimedBattles, now, cancellationToken);
            delayMs = BacklogDelayMs;
        }

        // Delay before next iteration
        await Task.Delay(TimeSpan.FromMilliseconds(delayMs), cancellationToken);
    }

    private async Task ProcessClaimedBattlesAsync(
        IServiceScope scope,
        IBattleStateStore stateStore,
        IReadOnlyList<ClaimedBattleDue> claimedBattles,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var turnAppService = scope.ServiceProvider.GetRequiredService<BattleTurnAppService>();

        _logger.LogDebug(
            "Processing {Count} claimed battles for deadline resolution",
            claimedBattles.Count);

        var resolvedCount = 0;
        var skippedStateMismatchCount = 0;
        var skippedMissingStateCount = 0;
        var transientErrorCount = 0;

        foreach (var claimed in claimedBattles)
        {
            var battleId = claimed.BattleId;
            var expectedTurnIndex = claimed.TurnIndex;

            try
            {
                // Load state to verify current turn index matches claim
                var state = await stateStore.GetStateAsync(battleId, cancellationToken);
                if (state == null)
                {
                    // State missing - already removed from ZSET by claim script
                    skippedMissingStateCount++;
                    _logger.LogDebug(
                        "Battle {BattleId} state missing after claim (already removed from ZSET)",
                        battleId);
                    continue;
                }

                // Verify turn index matches (state may have advanced)
                if (state.TurnIndex != expectedTurnIndex)
                {
                    // Turn already advanced - someone else resolved it
                    // This is expected and safe - do NOT re-add to ZSET
                    skippedStateMismatchCount++;
                    _logger.LogDebug(
                        "Battle {BattleId} turn advanced from {ExpectedTurn} to {ActualTurn} (already resolved by another worker)",
                        battleId, expectedTurnIndex, state.TurnIndex);
                    continue;
                }

                // Verify phase is still TurnOpen or Resolving (may have been transitioned)
                if (state.Phase != BattlePhase.TurnOpen && state.Phase != BattlePhase.Resolving)
                {
                    if (state.Phase == BattlePhase.Ended)
                    {
                        // Battle ended - already removed from ZSET by claim script
                        skippedStateMismatchCount++;
                        _logger.LogDebug(
                            "Battle {BattleId} already ended (already removed from ZSET)",
                            battleId);
                    }
                    else
                    {
                        // Unexpected phase - do NOT re-add to ZSET
                        skippedStateMismatchCount++;
                        _logger.LogDebug(
                            "Battle {BattleId} in unexpected phase {Phase} for turn {TurnIndex}",
                            battleId, state.Phase, expectedTurnIndex);
                    }
                    continue;
                }

                // Note: Deadline validation is handled by ClaimDueBattlesAsync Lua script.
                // If we got here, the battle was claimed and should be processed.
                // The claim script already validated phase and postponed non-TurnOpen battles.

                // Try to resolve the turn
                // BattleTurnAppService uses CAS to ensure only one resolution happens
                var resolved = await turnAppService.ResolveTurnAsync(battleId, cancellationToken);
                
                if (resolved)
                {
                    resolvedCount++;
                    
                    // Log at Info level when battle is actually resolved
                    _logger.LogInformation(
                        "Resolved turn {TurnIndex} for BattleId: {BattleId}",
                        expectedTurnIndex, battleId);
                }
                else
                {
                    // Resolution failed due to state mismatch (e.g., phase changed, turn advanced)
                    // This is expected when another worker/thread resolved it concurrently
                    // Do NOT re-add to ZSET - the state transition will have already added the next deadline
                    skippedStateMismatchCount++;
                    _logger.LogDebug(
                        "Turn {TurnIndex} resolution skipped for BattleId: {BattleId} (state mismatch - likely resolved by another worker)",
                        expectedTurnIndex, battleId);
                }
            }
            catch (Exception ex)
            {
                // Transient error (network, Redis, etc.)
                // Do NOT re-add to ZSET - ClaimDueBattlesAsync already postponed the deadline.
                // If worker crashes, the lease will expire and battle will become due again.
                transientErrorCount++;
                _logger.LogWarning(ex,
                    "Transient error processing battle {BattleId} turn {TurnIndex}. Battle will be retried after lease expires.",
                    battleId, expectedTurnIndex);
            }
        }

        // Log summary at Debug level per loop iteration
        _logger.LogDebug(
            "Processed {Total} claimed battles: {Resolved} resolved, {SkippedStateMismatch} skipped (state mismatch), {SkippedMissingState} skipped (missing state), {TransientErrors} transient errors",
            claimedBattles.Count, resolvedCount, skippedStateMismatchCount, skippedMissingStateCount, transientErrorCount);
    }

}


