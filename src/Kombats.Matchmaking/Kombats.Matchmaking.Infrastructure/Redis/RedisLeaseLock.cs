using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Kombats.Matchmaking.Infrastructure.Redis;

/// <summary>
/// Redis-based lease lock for multi-instance worker safety.
/// Uses SET NX PX pattern for atomic lock acquisition with TTL.
/// </summary>
public class RedisLeaseLock
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisLeaseLock> _logger;
    private readonly int _databaseIndex;

    public RedisLeaseLock(
        IConnectionMultiplexer redis,
        ILogger<RedisLeaseLock> logger,
        int databaseIndex = 1)
    {
        _redis = redis;
        _logger = logger;
        _databaseIndex = databaseIndex;
    }

    /// <summary>
    /// Attempts to acquire a lease lock for the given key.
    /// </summary>
    /// <param name="lockKey">The lock key (e.g., "mm:lease:matchmaking:default")</param>
    /// <param name="ttlMs">Time-to-live in milliseconds</param>
    /// <param name="lockValue">Unique value to identify this lock holder (e.g., instance ID)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if lock acquired, false otherwise</returns>
    public async Task<bool> TryAcquireLockAsync(
        string lockKey,
        int ttlMs,
        string lockValue,
        CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase(_databaseIndex);

        try
        {
            // SET key value NX PX ttlMs
            // NX = only set if not exists
            // PX = set expiration in milliseconds
            var acquired = await db.StringSetAsync(
                lockKey,
                lockValue,
                TimeSpan.FromMilliseconds(ttlMs),
                When.NotExists);

            if (acquired)
            {
                _logger.LogDebug(
                    "Acquired lease lock: Key={LockKey}, Value={LockValue}, TTL={TtlMs}ms",
                    lockKey, lockValue, ttlMs);
            }

            return acquired;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error acquiring lease lock: Key={LockKey}, Value={LockValue}",
                lockKey, lockValue);
            return false;
        }
    }

    /// <summary>
    /// Releases a lease lock (only if we own it).
    /// Uses Lua script for atomic check-and-delete.
    /// </summary>
    public async Task<bool> ReleaseLockAsync(
        string lockKey,
        string lockValue,
        CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase(_databaseIndex);

        try
        {
            // Lua script: only delete if value matches (prevents deleting someone else's lock)
            const string releaseScript = @"
                if redis.call('GET', KEYS[1]) == ARGV[1] then
                    return redis.call('DEL', KEYS[1])
                else
                    return 0
                end
            ";

            var result = await db.ScriptEvaluateAsync(
                releaseScript,
                new RedisKey[] { lockKey },
                new RedisValue[] { lockValue });

            var released = (int)result == 1;

            if (released)
            {
                _logger.LogDebug(
                    "Released lease lock: Key={LockKey}, Value={LockValue}",
                    lockKey, lockValue);
            }

            return released;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error releasing lease lock: Key={LockKey}, Value={LockValue}",
                lockKey, lockValue);
            return false;
        }
    }

    /// <summary>
    /// Gets the lock key for a variant.
    /// </summary>
    public static string GetLockKey(string variant) => $"mm:lease:matchmaking:{variant}";
}




