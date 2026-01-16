namespace Kombats.Matchmaking.Infrastructure.Redis;

/// <summary>
/// Centralized Lua scripts for Redis matchmaking operations.
/// All scripts are atomic and maintain consistency.
/// </summary>
internal static class RedisScripts
{
    /// <summary>
    /// Atomically adds a player to the queue (idempotent if already queued).
    /// KEYS[1] = queue list key (mm:queue:{variant})
    /// KEYS[2] = queued set key (mm:queued:{variant})
    /// ARGV[1] = playerId (string GUID)
    /// Returns: 1 if added, 0 if already in set (idempotent)
    /// </summary>
    internal const string JoinQueueScript = @"
        local queueKey = KEYS[1]
        local queuedKey = KEYS[2]
        local playerId = ARGV[1]
        
        -- Try to add to dedupe set (SADD returns 1 if added, 0 if already exists)
        local added = redis.call('SADD', queuedKey, playerId)
        
        if added == 1 then
            -- Added to set - add to queue list
            redis.call('RPUSH', queueKey, playerId)
        end
        
        return added
    ";

    /// <summary>
    /// Atomically removes a player from the queue using canceled set approach (idempotent if not in queue).
    /// KEYS[1] = queue list key (mm:queue:{variant})
    /// KEYS[2] = queued set key (mm:queued:{variant})
    /// KEYS[3] = canceled set key (mm:canceled:{variant})
    /// ARGV[1] = playerId (string GUID)
    /// Returns: 1 if removed, 0 if not in set (idempotent)
    /// Approach: Adds player to canceled set and removes from queued set.
    /// PopPair script will skip canceled players and clean them up.
    /// </summary>
    internal const string LeaveQueueScript = @"
        local queueKey = KEYS[1]
        local queuedKey = KEYS[2]
        local canceledKey = KEYS[3]
        local playerId = ARGV[1]
        
        -- Check if player is in queued set
        local inQueue = redis.call('SISMEMBER', queuedKey, playerId)
        
        if inQueue == 1 then
            -- Add to canceled set
            redis.call('SADD', canceledKey, playerId)
            -- Remove from queued set
            redis.call('SREM', queuedKey, playerId)
            return 1
        end
        
        -- Not in queue (idempotent)
        return 0
    ";

    /// <summary>
    /// Atomically pops a pair of players from the queue, skipping canceled players.
    /// KEYS[1] = queue list key (mm:queue:{variant})
    /// KEYS[2] = queued set key (mm:queued:{variant})
    /// KEYS[3] = canceled set key (mm:canceled:{variant})
    /// Returns: Array [playerAId, playerBId] if both found, empty array otherwise.
    /// Skips canceled players while popping and cleans them up.
    /// </summary>
    internal const string TryPopPairScript = @"
        local queueKey = KEYS[1]
        local queuedKey = KEYS[2]
        local canceledKey = KEYS[3]
        
        local firstPlayer = nil
        local secondPlayer = nil
        local maxAttempts = 100  -- Prevent infinite loop
        local attempts = 0
        
        -- Find first valid (non-canceled) player
        while not firstPlayer and attempts < maxAttempts do
            attempts = attempts + 1
            local candidate = redis.call('LPOP', queueKey)
            
            if not candidate then
                -- No more players in queue
                return {}
            end
            
            -- Check if canceled
            if redis.call('SISMEMBER', canceledKey, candidate) == 1 then
                -- Clean up: remove from canceled set
                redis.call('SREM', canceledKey, candidate)
                -- Continue to next player
            elseif redis.call('SISMEMBER', queuedKey, candidate) == 1 then
                -- Valid player in queue
                firstPlayer = candidate
            else
                -- Player not in queued set (shouldn't happen, but handle gracefully)
                -- Continue to next player
            end
        end
        
        if not firstPlayer then
            -- Couldn't find valid first player
            return {}
        end
        
        -- Find second valid (non-canceled) player
        attempts = 0
        while not secondPlayer and attempts < maxAttempts do
            attempts = attempts + 1
            local candidate = redis.call('LPOP', queueKey)
            
            if not candidate then
                -- No second player - push first back and return empty
                redis.call('LPUSH', queueKey, firstPlayer)
                return {}
            end
            
            -- Check if canceled
            if redis.call('SISMEMBER', canceledKey, candidate) == 1 then
                -- Clean up: remove from canceled set
                redis.call('SREM', canceledKey, candidate)
                -- Continue to next player
            elseif redis.call('SISMEMBER', queuedKey, candidate) == 1 then
                -- Valid player in queue
                secondPlayer = candidate
            else
                -- Player not in queued set (shouldn't happen, but handle gracefully)
                -- Continue to next player
            end
        end
        
        if not secondPlayer then
            -- Couldn't find valid second player - push first back
            redis.call('LPUSH', queueKey, firstPlayer)
            return {}
        end
        
        -- Both players found - remove from queued set and return pair
        redis.call('SREM', queuedKey, firstPlayer, secondPlayer)
        
        return {firstPlayer, secondPlayer}
    ";
}

