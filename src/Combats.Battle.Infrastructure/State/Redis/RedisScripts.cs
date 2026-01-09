namespace Combats.Battle.Infrastructure.State.Redis;

/// <summary>
/// Centralized Lua scripts for Redis battle state operations.
/// All scripts are atomic and maintain consistency between state JSON and deadlines ZSET.
/// 
/// Phase enum values (must match BattlePhase enum):
/// - ArenaOpen = 0
/// - TurnOpen = 1
/// - Resolving = 2
/// - Ended = 3
/// </summary>
internal static class RedisScripts
{
    /// <summary>
    /// Opens a turn for a battle atomically.
    /// KEYS[1] = state key (battle:state:{battleId})
    /// KEYS[2] = deadlines ZSET key (battle:deadlines)
    /// ARGV[1] = turnIndex (integer)
    /// ARGV[2] = deadlineUnixMs (long, unix milliseconds)
    /// ARGV[3] = battleId (string)
    /// Returns: 1 if successful, 0 if not committed
    /// </summary>
    internal const string TryOpenTurnScript = @"
        local stateJson = redis.call('GET', KEYS[1])
        if not stateJson then
            return 0
        end
        local state = cjson.decode(stateJson)
        -- Cannot open if already ended (Ended=3)
        if state.Phase == 3 then
            return 0
        end
        -- Must open turn N only if LastResolvedTurnIndex == N-1
        local expectedLastResolved = tonumber(ARGV[1]) - 1
        if state.LastResolvedTurnIndex ~= expectedLastResolved then
            return 0
        end
        -- Must be in ArenaOpen (0) or Resolving (2) phase
        if state.Phase ~= 0 and state.Phase ~= 2 then
            return 0
        end
        -- Set to TurnOpen (1)
        state.Phase = 1
        state.TurnIndex = tonumber(ARGV[1])
        state.DeadlineUtcTicks = tonumber(ARGV[2]) * 10000  -- Convert unixMs to ticks for state
        state.Version = state.Version + 1
        redis.call('SET', KEYS[1], cjson.encode(state))
        -- Add to deadlines ZSET atomically (using unixMs as score)
        redis.call('ZADD', KEYS[2], ARGV[2], ARGV[3])
        return 1
    ";

    /// <summary>
    /// Marks a turn as resolving.
    /// KEYS[1] = state key (battle:state:{battleId})
    /// ARGV[1] = turnIndex (integer)
    /// Returns: 1 if successful, 0 if not committed
    /// </summary>
    internal const string TryMarkTurnResolvingScript = @"
        local stateJson = redis.call('GET', KEYS[1])
        if not stateJson then
            return 0
        end
        local state = cjson.decode(stateJson)
        -- Must be in TurnOpen (1) phase and turnIndex must match, and not ended
        if state.Phase ~= 1 or state.TurnIndex ~= tonumber(ARGV[1]) then
            return 0
        end
        -- Set to Resolving (2)
        state.Phase = 2
        state.Version = state.Version + 1
        redis.call('SET', KEYS[1], cjson.encode(state))
        return 1
    ";

    /// <summary>
    /// Marks current turn as resolved and opens the next turn atomically.
    /// KEYS[1] = state key (battle:state:{battleId})
    /// KEYS[2] = deadlines ZSET key (battle:deadlines)
    /// ARGV[1] = currentTurnIndex (integer)
    /// ARGV[2] = nextTurnIndex (integer)
    /// ARGV[3] = nextDeadlineUnixMs (long, unix milliseconds)
    /// ARGV[4] = noActionStreak (integer)
    /// ARGV[5] = playerAHp (integer)
    /// ARGV[6] = playerBHp (integer)
    /// ARGV[7] = battleId (string)
    /// Returns: 1 if successful, 0 if not committed
    /// </summary>
    internal const string MarkTurnResolvedAndOpenNextScript = @"
        local stateJson = redis.call('GET', KEYS[1])
        if not stateJson then
            return 0
        end
        local state = cjson.decode(stateJson)
        -- Must be in Resolving (2) phase and current turnIndex must match
        if state.Phase ~= 2 or state.TurnIndex ~= tonumber(ARGV[1]) then
            return 0
        end
        -- Set LastResolvedTurnIndex to resolved turnIndex
        state.LastResolvedTurnIndex = tonumber(ARGV[1])
        -- Set to TurnOpen (1) for next turn
        state.Phase = 1
        state.TurnIndex = tonumber(ARGV[2])
        state.DeadlineUtcTicks = tonumber(ARGV[3]) * 10000  -- Convert unixMs to ticks for state
        state.NoActionStreakBoth = tonumber(ARGV[4])
        -- Update HP atomically with turn resolution
        state.PlayerAHp = tonumber(ARGV[5])
        state.PlayerBHp = tonumber(ARGV[6])
        -- Clear NextResolveScheduledUtcTicks (deprecated, kept for backward compatibility)
        state.NextResolveScheduledUtcTicks = 0
        state.Version = state.Version + 1
        redis.call('SET', KEYS[1], cjson.encode(state))
        -- Update deadlines ZSET atomically (using unixMs as score)
        redis.call('ZADD', KEYS[2], ARGV[3], ARGV[7])
        return 1
    ";

    /// <summary>
    /// Ends a battle and marks the current turn as resolved atomically.
    /// KEYS[1] = state key (battle:state:{battleId})
    /// KEYS[2] = active battles set key (battle:active)
    /// KEYS[3] = deadlines ZSET key (battle:deadlines)
    /// ARGV[1] = turnIndex (integer)
    /// ARGV[2] = noActionStreak (integer)
    /// ARGV[3] = playerAHp (integer)
    /// ARGV[4] = playerBHp (integer)
    /// ARGV[5] = battleId (string)
    /// Returns: 2 = AlreadyEnded, 1 = EndedNow, 0 = NotCommitted
    /// </summary>
    internal const string EndBattleAndMarkResolvedScript = @"
        local stateJson = redis.call('GET', KEYS[1])
        if not stateJson then
            return 0
        end
        local state = cjson.decode(stateJson)
        -- If already ended (Ended=3), return AlreadyEnded (2)
        if state.Phase == 3 then
            return 2
        end
        -- Only end if currently resolving (Resolving=2) the specified turn
        if state.Phase ~= 2 or state.TurnIndex ~= tonumber(ARGV[1]) then
            return 0
        end
        -- Set to Ended (3)
        state.Phase = 3
        state.LastResolvedTurnIndex = tonumber(ARGV[1])
        state.NoActionStreakBoth = tonumber(ARGV[2])
        -- Update HP atomically with battle end
        state.PlayerAHp = tonumber(ARGV[3])
        state.PlayerBHp = tonumber(ARGV[4])
        state.NextResolveScheduledUtcTicks = 0
        state.Version = state.Version + 1
        redis.call('SET', KEYS[1], cjson.encode(state))
        -- Remove from active battles set
        redis.call('SREM', KEYS[2], ARGV[5])
        -- Remove from deadlines ZSET atomically
        redis.call('ZREM', KEYS[3], ARGV[5])
        return 1
    ";

    /// <summary>
    /// Claims due battles from the deadlines ZSET atomically using Redis locks.
    /// 
    /// Rules:
    /// 1. If state.Phase == Ended (3): ZREM deadlines and skip.
    /// 2. If state.Phase != TurnOpen (1): postpone by smallDelayMs (no claim).
    /// 3. If state.DeadlineUtcTicks exists and is in the future: set ZSET score to deadline in unixMs (no claim).
    /// 4. If phase TurnOpen (1): attempt lock; if lock acquired: postpone to now + leaseWindowMs.
    /// 
    /// KEYS[1] = deadlines ZSET key (battle:deadlines)
    /// ARGV[1] = nowUnixMs (long, unix milliseconds)
    /// ARGV[2] = limit (integer, max battles to process)
    /// ARGV[3] = leaseWindowMs (integer, milliseconds for lease TTL)
    /// ARGV[4] = smallDelayMs (integer, milliseconds to postpone non-TurnOpen battles)
    /// ARGV[5] = stateKeyPrefix (string, "battle:state:")
    /// 
    /// Returns: Array of pairs [battleId1, turnIndex1, battleId2, turnIndex2, ...]
    /// </summary>
    internal const string ClaimDueBattlesScript = @"
        local nowUnixMs = tonumber(ARGV[1])
        local limit = tonumber(ARGV[2])
        local leaseWindowMs = tonumber(ARGV[3])
        local smallDelayMs = tonumber(ARGV[4])
        local stateKeyPrefix = ARGV[5]
        local deadlinesKey = KEYS[1]
        
        -- Convert nowUnixMs to ticks for state comparison (state stores deadline as ticks)
        local nowTicks = nowUnixMs * 10000
        
        -- Get due battles from ZSET (up to limit)
        -- Note: ZSET scores are in unixMs, so we compare directly
        local dueMembers = redis.call('ZRANGEBYSCORE', deadlinesKey, '-inf', nowUnixMs, 'LIMIT', 0, limit)
        
        local claimed = {}
        local claimedCount = 0
        
        for i = 1, #dueMembers do
            local battleIdStr = dueMembers[i]
            local stateKey = stateKeyPrefix .. battleIdStr
            
            -- Read battle state
            local stateJson = redis.call('GET', stateKey)
            
            if not stateJson then
                -- State missing - remove from ZSET and skip
                redis.call('ZREM', deadlinesKey, battleIdStr)
            else
                local success, state = pcall(cjson.decode, stateJson)
                if not success or not state or not state.TurnIndex then
                    -- Invalid JSON or missing TurnIndex - remove from ZSET and skip
                    redis.call('ZREM', deadlinesKey, battleIdStr)
                else
                    local turnIndex = state.TurnIndex
                    local phase = state.Phase
                    
                    -- Rule 1: If state.Phase == Ended (3): ZREM and skip
                    if phase == 3 then
                        redis.call('ZREM', deadlinesKey, battleIdStr)
                    -- Rule 3: If state.DeadlineUtcTicks exists and is in the future, use it as authoritative
                    elseif state.DeadlineUtcTicks and state.DeadlineUtcTicks > nowTicks then
                        -- Convert state deadline (ticks) to unixMs for ZSET score
                        local deadlineUnixMs = math.floor(state.DeadlineUtcTicks / 10000)
                        redis.call('ZADD', deadlinesKey, deadlineUnixMs, battleIdStr)
                    -- Rule 2: If state.Phase != TurnOpen (1): postpone by smallDelayMs
                    elseif phase ~= 1 then
                        local postponeUnixMs = nowUnixMs + smallDelayMs
                        redis.call('ZADD', deadlinesKey, postponeUnixMs, battleIdStr)
                    -- Rule 4: Phase == TurnOpen (1) - attempt to acquire lock
                    else
                        local lockKey = 'lock:battle:' .. battleIdStr .. ':turn:' .. turnIndex
                        local lockAcquired = redis.call('SET', lockKey, '1', 'NX', 'PX', leaseWindowMs)
                        
                        if lockAcquired then
                            -- Lock acquired - postpone ZSET score to lease window end (for crash recovery)
                            local postponeUnixMs = nowUnixMs + leaseWindowMs
                            redis.call('ZADD', deadlinesKey, postponeUnixMs, battleIdStr)
                            claimedCount = claimedCount + 1
                            claimed[claimedCount * 2 - 1] = battleIdStr
                            claimed[claimedCount * 2] = tostring(turnIndex)
                        end
                        -- If lock not acquired, another worker is processing this turn - skip silently
                    end
                end
            end
        end
        
        return claimed
    ";
}

