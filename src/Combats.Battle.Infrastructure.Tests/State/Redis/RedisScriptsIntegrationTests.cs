using System.Text.Json;
using Combats.Battle.Domain.Model;
using Combats.Battle.Domain.Rules;
using Combats.Battle.Infrastructure.State.Redis;
using FluentAssertions;
using StackExchange.Redis;
using Xunit;

namespace Combats.Battle.Infrastructure.Tests.State.Redis;

/// <summary>
/// Integration tests for Redis Lua scripts with DeadlineUnixMs.
/// These tests require a running Redis instance (e.g., via docker-compose).
/// Skip tests if Redis is not available.
/// </summary>
public class RedisScriptsIntegrationTests : IDisposable
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly bool _redisAvailable;

    public RedisScriptsIntegrationTests()
    {
        try
        {
            _redis = ConnectionMultiplexer.Connect("localhost:6379");
            _db = _redis.GetDatabase();
            
            // Test connection
            _db.StringSet("__test__", "ok");
            _redisAvailable = _db.StringGet("__test__") == "ok";
            _db.KeyDelete("__test__");
        }
        catch
        {
            _redisAvailable = false;
            _redis = null!;
            _db = null!;
        }
    }

    private void SkipIfRedisUnavailable()
    {
        if (!_redisAvailable)
        {
            throw new SkipException("Redis is not available. Start Redis with: docker-compose up redis");
        }
    }

    [Fact]
    public void TryOpenTurnScript_ShouldSetDeadlineUnixMs_InStateAndZSet()
    {
        SkipIfRedisUnavailable();

        // Arrange
        var battleId = Guid.NewGuid();
        var stateKey = $"battle:state:{battleId}";
        var deadlinesKey = "battle:deadlines";
        
        // Create initial state
        var initialState = new BattleState
        {
            BattleId = battleId,
            PlayerAId = Guid.NewGuid(),
            PlayerBId = Guid.NewGuid(),
            Ruleset = new Ruleset(1, 10, 3, 123),
            Phase = BattlePhase.ArenaOpen,
            TurnIndex = 0,
            DeadlineUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            NoActionStreakBoth = 0,
            LastResolvedTurnIndex = 0,
            MatchId = Guid.NewGuid(),
            Version = 1,
            PlayerAHp = 100,
            PlayerBHp = 100
        };

        var initialStateJson = JsonSerializer.Serialize(initialState);
        _db.StringSet(stateKey, initialStateJson);

        var turnIndex = 1;
        var deadlineUnixMs = DateTimeOffset.UtcNow.AddSeconds(30).ToUnixTimeMilliseconds();

        // Act
        var result = _db.ScriptEvaluate(
            RedisScripts.TryOpenTurnScript,
            new RedisKey[] { stateKey, deadlinesKey },
            new RedisValue[] { turnIndex, deadlineUnixMs, battleId.ToString() });

        // Assert
        ((int)result).Should().Be(1, "script should succeed");

        // Verify state JSON contains DeadlineUnixMs (not DeadlineUtcTicks)
        var stateJson = _db.StringGet(stateKey);
        stateJson.HasValue.Should().BeTrue();
        stateJson.ToString().Should().Contain("\"DeadlineUnixMs\":" + deadlineUnixMs);
        stateJson.ToString().Should().NotContain("DeadlineUtcTicks");
        stateJson.ToString().Should().NotContain("E"); // No scientific notation

        // Verify ZSET score matches DeadlineUnixMs
        var zsetScore = _db.SortedSetScore(deadlinesKey, battleId.ToString());
        zsetScore.HasValue.Should().BeTrue();
        zsetScore!.Value.Should().Be(deadlineUnixMs);

        // Cleanup
        _db.KeyDelete(stateKey);
        _db.SortedSetRemove(deadlinesKey, battleId.ToString());
    }

    [Fact]
    public void ClaimDueBattlesScript_WithFutureDeadline_ShouldNotClaim_AndUpdateZSet()
    {
        SkipIfRedisUnavailable();

        // Arrange
        var battleId = Guid.NewGuid();
        var stateKey = $"battle:state:{battleId}";
        var deadlinesKey = "battle:deadlines";
        
        var nowUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var futureDeadlineUnixMs = nowUnixMs + 60000; // 1 minute in the future

        // Create state with future deadline
        var state = new BattleState
        {
            BattleId = battleId,
            PlayerAId = Guid.NewGuid(),
            PlayerBId = Guid.NewGuid(),
            Ruleset = new Ruleset(1, 10, 3, 123),
            Phase = BattlePhase.TurnOpen,
            TurnIndex = 1,
            DeadlineUnixMs = futureDeadlineUnixMs,
            NoActionStreakBoth = 0,
            LastResolvedTurnIndex = 0,
            MatchId = Guid.NewGuid(),
            Version = 1,
            PlayerAHp = 100,
            PlayerBHp = 100
        };

        var stateJson = JsonSerializer.Serialize(state);
        _db.StringSet(stateKey, stateJson);
        
        // Set ZSET with past deadline (should be updated by script)
        _db.SortedSetAdd(deadlinesKey, battleId.ToString(), nowUnixMs - 1000);

        // Act
        var result = _db.ScriptEvaluate(
            RedisScripts.ClaimDueBattlesScript,
            new RedisKey[] { deadlinesKey },
            new RedisValue[] { nowUnixMs, 10, 30000, 200, "battle:state:" });

        // Assert
        var claimed = (RedisValue[]?)result;
        claimed.Should().NotBeNull();
        claimed!.Length.Should().Be(0, "battle should not be claimed when deadline is in the future");

        // Verify ZSET score was updated to future deadline
        var zsetScore = _db.SortedSetScore(deadlinesKey, battleId.ToString());
        zsetScore.HasValue.Should().BeTrue();
        zsetScore!.Value.Should().Be(futureDeadlineUnixMs);

        // Cleanup
        _db.KeyDelete(stateKey);
        _db.SortedSetRemove(deadlinesKey, battleId.ToString());
    }

    [Fact]
    public void ClaimDueBattlesScript_WithPastDeadline_ShouldClaim_AndPostponeZSet()
    {
        SkipIfRedisUnavailable();

        // Arrange
        var battleId = Guid.NewGuid();
        var stateKey = $"battle:state:{battleId}";
        var deadlinesKey = "battle:deadlines";
        var lockKey = $"lock:battle:{battleId}:turn:1";
        
        var nowUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var pastDeadlineUnixMs = nowUnixMs - 5000; // 5 seconds in the past
        var leaseWindowMs = 30000;

        // Create state with past deadline
        var state = new BattleState
        {
            BattleId = battleId,
            PlayerAId = Guid.NewGuid(),
            PlayerBId = Guid.NewGuid(),
            Ruleset = new Ruleset(1, 10, 3, 123),
            Phase = BattlePhase.TurnOpen,
            TurnIndex = 1,
            DeadlineUnixMs = pastDeadlineUnixMs,
            NoActionStreakBoth = 0,
            LastResolvedTurnIndex = 0,
            MatchId = Guid.NewGuid(),
            Version = 1,
            PlayerAHp = 100,
            PlayerBHp = 100
        };

        var stateJson = JsonSerializer.Serialize(state);
        _db.StringSet(stateKey, stateJson);
        _db.SortedSetAdd(deadlinesKey, battleId.ToString(), pastDeadlineUnixMs);

        // Act
        var result = _db.ScriptEvaluate(
            RedisScripts.ClaimDueBattlesScript,
            new RedisKey[] { deadlinesKey },
            new RedisValue[] { nowUnixMs, 10, leaseWindowMs, 200, "battle:state:" });

        // Assert
        var claimed = (RedisValue[]?)result;
        claimed.Should().NotBeNull();
        claimed!.Length.Should().Be(2, "should return [battleId, turnIndex]");
        claimed[0].ToString().Should().Be(battleId.ToString());
        claimed[1].ToString().Should().Be("1");

        // Verify lock was acquired
        var lockValue = _db.StringGet(lockKey);
        lockValue.HasValue.Should().BeTrue();

        // Verify ZSET score was postponed to now + leaseWindowMs
        var zsetScore = _db.SortedSetScore(deadlinesKey, battleId.ToString());
        zsetScore.HasValue.Should().BeTrue();
        zsetScore!.Value.Should().BeApproximately(nowUnixMs + leaseWindowMs, 1000, "should postpone to now + leaseWindowMs");

        // Cleanup
        _db.KeyDelete(stateKey);
        _db.KeyDelete(lockKey);
        _db.SortedSetRemove(deadlinesKey, battleId.ToString());
    }

    [Fact]
    public void MarkTurnResolvedAndOpenNextScript_ShouldSetDeadlineUnixMs_InStateAndZSet()
    {
        SkipIfRedisUnavailable();

        // Arrange
        var battleId = Guid.NewGuid();
        var stateKey = $"battle:state:{battleId}";
        var deadlinesKey = "battle:deadlines";
        
        // Create state in Resolving phase
        var initialState = new BattleState
        {
            BattleId = battleId,
            PlayerAId = Guid.NewGuid(),
            PlayerBId = Guid.NewGuid(),
            Ruleset = new Ruleset(1, 10, 3, 123),
            Phase = BattlePhase.Resolving,
            TurnIndex = 1,
            DeadlineUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            NoActionStreakBoth = 0,
            LastResolvedTurnIndex = 0,
            MatchId = Guid.NewGuid(),
            Version = 1,
            PlayerAHp = 100,
            PlayerBHp = 100
        };

        var initialStateJson = JsonSerializer.Serialize(initialState);
        _db.StringSet(stateKey, initialStateJson);

        var currentTurnIndex = 1;
        var nextTurnIndex = 2;
        var nextDeadlineUnixMs = DateTimeOffset.UtcNow.AddSeconds(30).ToUnixTimeMilliseconds();
        var noActionStreak = 0;
        var playerAHp = 95;
        var playerBHp = 90;

        // Act
        var result = _db.ScriptEvaluate(
            RedisScripts.MarkTurnResolvedAndOpenNextScript,
            new RedisKey[] { stateKey, deadlinesKey },
            new RedisValue[] { 
                currentTurnIndex, 
                nextTurnIndex, 
                nextDeadlineUnixMs, 
                noActionStreak,
                playerAHp,
                playerBHp,
                battleId.ToString()
            });

        // Assert
        ((int)result).Should().Be(1, "script should succeed");

        // Verify state JSON contains DeadlineUnixMs
        var stateJson = _db.StringGet(stateKey);
        stateJson.HasValue.Should().BeTrue();
        var deserializedState = JsonSerializer.Deserialize<BattleState>(stateJson.ToString());
        deserializedState.Should().NotBeNull();
        deserializedState!.DeadlineUnixMs.Should().Be(nextDeadlineUnixMs);
        deserializedState.Phase.Should().Be(BattlePhase.TurnOpen);
        deserializedState.TurnIndex.Should().Be(nextTurnIndex);
        deserializedState.PlayerAHp.Should().Be(playerAHp);
        deserializedState.PlayerBHp.Should().Be(playerBHp);

        // Verify ZSET score matches DeadlineUnixMs
        var zsetScore = _db.SortedSetScore(deadlinesKey, battleId.ToString());
        zsetScore.HasValue.Should().BeTrue();
        zsetScore!.Value.Should().Be(nextDeadlineUnixMs);

        // Cleanup
        _db.KeyDelete(stateKey);
        _db.SortedSetRemove(deadlinesKey, battleId.ToString());
    }

    public void Dispose()
    {
        _redis?.Dispose();
    }
}


