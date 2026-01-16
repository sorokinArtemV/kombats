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
        var matchRepository = serviceProvider.GetRequiredService<IMatchRepository>();

        DateTimeOffset timeoutThreshold = DateTimeOffset.UtcNow.AddSeconds(-_options.TimeoutSeconds);
        var timedOutMatches = await dbContext.Matches
            .Where(m => m.State == (int)MatchState.BattleCreateRequested && m.UpdatedAtUtc < timeoutThreshold)
            .Select(m => new { m.MatchId, m.BattleId, m.PlayerAId, m.PlayerBId, m.UpdatedAtUtc })
            .ToListAsync(cancellationToken);

        if (timedOutMatches.Count == 0) return;
        

        _logger.LogWarning(
            "Found {Count} matches stuck in BattleCreateRequested state older than {TimeoutSeconds}s",
            timedOutMatches.Count, _options.TimeoutSeconds);

        foreach (var match in timedOutMatches)
        {
            try
            {
                var updatedAt = DateTimeOffset.UtcNow;
                await matchRepository.UpdateStateAsync(
                    match.MatchId,
                    MatchState.TimedOut,
                    updatedAt,
                    cancellationToken);

                _logger.LogWarning(
                    "Marked match as TimedOut: MatchId={MatchId}, BattleId={BattleId}, PlayerA={PlayerAId}, PlayerB={PlayerBId}, StuckSince={StuckSince}",
                    match.MatchId, match.BattleId, match.PlayerAId, match.PlayerBId, match.UpdatedAtUtc);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to mark match as TimedOut: MatchId={MatchId}, BattleId={BattleId}",
                    match.MatchId, match.BattleId);
            }
        }
    }
}


