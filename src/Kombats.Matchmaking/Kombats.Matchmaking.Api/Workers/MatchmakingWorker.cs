using Kombats.Matchmaking.Application.UseCases;
using Kombats.Matchmaking.Infrastructure.Options;
using Kombats.Matchmaking.Infrastructure.Redis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Kombats.Matchmaking.Api.Workers;

/// <summary>
/// Background service that performs matchmaking ticks at configured intervals.
/// Uses Redis lease lock for multi-instance safety.
/// </summary>
public sealed class MatchmakingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<MatchmakingWorker> _logger;
    private readonly ILogger<RedisLeaseLock> _leaseLockLogger;
    private readonly MatchmakingWorkerOptions _options;
    private readonly string _instanceId;

    public MatchmakingWorker(
        IServiceScopeFactory scopeFactory,
        IConnectionMultiplexer redis,
        ILogger<MatchmakingWorker> logger,
        ILogger<RedisLeaseLock> leaseLockLogger,
        IOptions<MatchmakingWorkerOptions> options)
    {
        _scopeFactory = scopeFactory;
        _redis = redis;
        _logger = logger;
        _leaseLockLogger = leaseLockLogger;
        _options = options.Value;
        _instanceId = $"{Environment.MachineName}-{Guid.NewGuid():N}";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "MatchmakingWorker started. InstanceId={InstanceId}, TickDelay={TickDelayMs}ms",
            _instanceId, _options.TickDelayMs);

        const string variant = "default";
        var lockKey = RedisLeaseLock.GetLockKey(variant);
        const int lockTtlMs = 5000; // Lock expires after 5 seconds (must be renewed)

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var leaseLock = new RedisLeaseLock(_redis, _leaseLockLogger, 1);

                // Try to acquire lease lock for this variant
                var lockAcquired = await leaseLock.TryAcquireLockAsync(
                    lockKey,
                    lockTtlMs,
                    _instanceId,
                    stoppingToken);

                if (!lockAcquired)
                {
                    // Another instance has the lock - sleep and retry
                    _logger.LogDebug(
                        "Lease lock not acquired for variant {Variant}, sleeping. InstanceId={InstanceId}",
                        variant, _instanceId);
                    await Task.Delay(TimeSpan.FromMilliseconds(_options.TickDelayMs), stoppingToken);
                    continue;
                }

                // We have the lock - perform matchmaking tick
                var matchmakingService = scope.ServiceProvider.GetRequiredService<MatchmakingService>();
                var result = await matchmakingService.MatchmakingTickAsync(variant, stoppingToken);

                if (result.Type == MatchCreatedResultType.MatchCreated && result.MatchInfo != null)
                {
                    _logger.LogInformation(
                        "Match created: MatchId={MatchId}, BattleId={BattleId}, PlayerA={PlayerAId}, PlayerB={PlayerBId}, InstanceId={InstanceId}",
                        result.MatchInfo.MatchId,
                        result.MatchInfo.BattleId,
                        result.MatchInfo.PlayerAId,
                        result.MatchInfo.PlayerBId,
                        _instanceId);
                }

                // Release lock (optional - it will expire anyway, but good practice)
                await leaseLock.ReleaseLockAsync(lockKey, _instanceId, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error in MatchmakingWorker tick. InstanceId={InstanceId}",
                    _instanceId);
            }

            // Wait for configured delay before next tick
            await Task.Delay(TimeSpan.FromMilliseconds(_options.TickDelayMs), stoppingToken);
        }

        _logger.LogInformation(
            "MatchmakingWorker stopped. InstanceId={InstanceId}",
            _instanceId);
    }
}

