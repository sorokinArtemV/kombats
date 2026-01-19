using Kombats.Matchmaking.Application.Abstractions;
using Kombats.Matchmaking.Domain;
using Kombats.Matchmaking.Infrastructure.Data;
using Kombats.Matchmaking.Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Kombats.Matchmaking.Api.Workers;

/// <summary>
/// Background service that scans for matches stuck in BattleCreateRequested state and marks them as TimedOut.
/// </summary>
public sealed class MatchTimeoutWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MatchTimeoutWorker> _logger;
    private readonly MatchTimeoutWorkerOptions _options;

    public MatchTimeoutWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<MatchTimeoutWorker> logger,
        IOptions<MatchTimeoutWorkerOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "MatchTimeoutWorker started. ScanInterval: {ScanIntervalMs}ms, TimeoutThreshold: {TimeoutSeconds}s",
            _options.ScanIntervalMs, _options.TimeoutSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await ScanAndMarkTimedOutMatchesAsync(scope.ServiceProvider, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in MatchTimeoutWorker scan");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(_options.ScanIntervalMs), stoppingToken);
        }

        _logger.LogInformation("MatchTimeoutWorker stopped");
    }

    private async Task ScanAndMarkTimedOutMatchesAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var dbContext = serviceProvider.GetRequiredService<MatchmakingDbContext>();

        var nowUtc = DateTimeOffset.UtcNow;
        var timeoutThreshold = nowUtc.AddSeconds(-_options.TimeoutSeconds);
        
        // Load entities that need to be timed out
        var timedOutEntities = await dbContext.Matches
            .Where(m => m.State == (int)MatchState.BattleCreateRequested && m.UpdatedAtUtc < timeoutThreshold)
            .ToListAsync(cancellationToken);

        if (timedOutEntities.Count == 0) return;

        _logger.LogWarning(
            "Found {Count} matches stuck in BattleCreateRequested state older than {TimeoutSeconds}s",
            timedOutEntities.Count, _options.TimeoutSeconds);

        // Batch update: mark all entities in memory, then save once
        foreach (var entity in timedOutEntities)
        {
            entity.State = (int)MatchState.TimedOut;
            entity.UpdatedAtUtc = nowUtc; // Use DateTimeOffset.UtcNow directly (offset is already 0)
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);

            // Log successful updates
            foreach (var entity in timedOutEntities)
            {
                _logger.LogWarning(
                    "Marked match as TimedOut: MatchId={MatchId}, BattleId={BattleId}, PlayerA={PlayerAId}, PlayerB={PlayerBId}, StuckSince={StuckSince}",
                    entity.MatchId, entity.BattleId, entity.PlayerAId, entity.PlayerBId, timeoutThreshold);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to save timed out matches batch. Count={Count}",
                timedOutEntities.Count);
            throw;
        }
    }
}


