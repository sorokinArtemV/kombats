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
            "MatchmakingWorker started. InstanceId={InstanceId}, TickDelay={TickDelayMs}ms, Variants=[{Variants}], LockTtl={LockTtlMs}ms, RedisDb={RedisDb}",
            _instanceId,
            _options.TickDelayMs,
            string.Join(", ", _options.Variants),
            _options.ClaimLeaseTtlMs,
            _options.RedisDatabaseIndex);

        // Construct RedisLeaseLock once per worker (database index is fixed)
        var leaseLock = new RedisLeaseLock(_redis, _leaseLockLogger, _options.RedisDatabaseIndex);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Process each variant
                foreach (var variant in _options.Variants)
                {
                    if (stoppingToken.IsCancellationRequested)
                        break;

                    var lockKey = RedisLeaseLock.GetLockKey(variant);

                    // Try to acquire lease lock for this variant
                    var lockAcquired = await leaseLock.TryAcquireLockAsync(
                        lockKey,
                        _options.ClaimLeaseTtlMs,
                        _instanceId,
                        stoppingToken);

                    if (!lockAcquired)
                    {
                        // Another instance has the lock - skip this variant and continue
                        _logger.LogDebug(
                            "Lease lock not acquired for variant {Variant}, skipping. InstanceId={InstanceId}",
                            variant, _instanceId);
                        continue;
                    }

                    try
                    {
                        // We have the lock - perform matchmaking tick
                        // Note: The critical section must complete within ClaimLeaseTtlMs
                        using var scope = _scopeFactory.CreateScope();
                        var matchmakingService = scope.ServiceProvider.GetRequiredService<MatchmakingService>();
                        var result = await matchmakingService.MatchmakingTickAsync(variant, stoppingToken);

                        if (result.Type == MatchCreatedResultType.MatchCreated && result.MatchInfo != null)
                        {
                            _logger.LogInformation(
                                "Match created: MatchId={MatchId}, BattleId={BattleId}, PlayerA={PlayerAId}, PlayerB={PlayerBId}, Variant={Variant}, InstanceId={InstanceId}",
                                result.MatchInfo.MatchId,
                                result.MatchInfo.BattleId,
                                result.MatchInfo.PlayerAId,
                                result.MatchInfo.PlayerBId,
                                variant,
                                _instanceId);
                        }
                    }
                    finally
                    {
                        // Release lock (optional - it will expire anyway, but good practice)
                        await leaseLock.ReleaseLockAsync(lockKey, _instanceId, stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error in MatchmakingWorker tick. InstanceId={InstanceId}",
                    _instanceId);
            }

            // Wait for configured delay before next tick (with jitter to reduce thundering herd)
            // Jitter: ±20% of TickDelayMs to smooth load distribution across instances
            var jitterRange = (int)(_options.TickDelayMs * 0.2);
            var jitter = Random.Shared.Next(-jitterRange, jitterRange + 1);
            var delayMs = Math.Max(1, _options.TickDelayMs + jitter); // Ensure at least 1ms delay
            await Task.Delay(TimeSpan.FromMilliseconds(delayMs), stoppingToken);
        }

        _logger.LogInformation(
            "MatchmakingWorker stopped. InstanceId={InstanceId}",
            _instanceId);
    }
}

