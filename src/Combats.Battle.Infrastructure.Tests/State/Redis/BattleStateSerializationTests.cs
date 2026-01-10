using System.Text.Json;
using Combats.Battle.Domain.Model;
using Combats.Battle.Domain.Rules;
using Combats.Battle.Infrastructure.State.Redis;
using FluentAssertions;
using Xunit;

namespace Combats.Battle.Infrastructure.Tests.State.Redis;

/// <summary>
/// Unit tests for BattleState serialization/deserialization with DeadlineUnixMs.
/// Verifies that large unixMs values serialize without scientific notation and deserialize correctly.
/// </summary>
public class BattleStateSerializationTests
{
    [Fact]
    public void Serialize_WithLargeUnixMs_ShouldNotUseScientificNotation()
    {
        // Arrange
        var state = new BattleState
        {
            BattleId = Guid.NewGuid(),
            PlayerAId = Guid.NewGuid(),
            PlayerBId = Guid.NewGuid(),
            Ruleset = new Ruleset(1, 10, 3, 123),
            Phase = BattlePhase.TurnOpen,
            TurnIndex = 1,
            DeadlineUnixMs = 1768018678268, // Realistic unixMs value (no scientific notation)
            NoActionStreakBoth = 0,
            LastResolvedTurnIndex = 0,
            MatchId = Guid.NewGuid(),
            Version = 1,
            PlayerAHp = 100,
            PlayerBHp = 100
        };

        // Act
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = false });

        // Assert
        json.Should().Contain("\"DeadlineUnixMs\":1768018678268");
        json.Should().NotContain("E");
        json.Should().NotContain("e");
        json.Should().NotContain("1.768");
    }

    [Fact]
    public void Deserialize_WithLargeUnixMs_ShouldSucceed()
    {
        // Arrange
        var originalDeadlineUnixMs = 1768018678268L;
        var json = $$"""
        {
            "BattleId": "{{Guid.NewGuid()}}",
            "PlayerAId": "{{Guid.NewGuid()}}",
            "PlayerBId": "{{Guid.NewGuid()}}",
            "Ruleset": {
                "Version": 1,
                "TurnSeconds": 10,
                "NoActionLimit": 3,
                "Seed": 123
            },
            "Phase": 1,
            "TurnIndex": 1,
            "DeadlineUnixMs": {{originalDeadlineUnixMs}},
            "NoActionStreakBoth": 0,
            "LastResolvedTurnIndex": 0,
            "MatchId": "{{Guid.NewGuid()}}",
            "Version": 1,
            "PlayerAHp": 100,
            "PlayerBHp": 100
        }
        """;

        // Act
        var state = JsonSerializer.Deserialize<BattleState>(json);

        // Assert
        state.Should().NotBeNull();
        state!.DeadlineUnixMs.Should().Be(originalDeadlineUnixMs);
        var deadlineUtc = state.GetDeadlineUtc();
        deadlineUtc.Should().BeCloseTo(DateTimeOffset.FromUnixTimeMilliseconds(originalDeadlineUnixMs).UtcDateTime, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public void Serialize_And_Deserialize_RoundTrip_ShouldPreserveValues()
    {
        // Arrange
        var originalState = new BattleState
        {
            BattleId = Guid.NewGuid(),
            PlayerAId = Guid.NewGuid(),
            PlayerBId = Guid.NewGuid(),
            Ruleset = new Ruleset(1, 10, 3, 123),
            Phase = BattlePhase.TurnOpen,
            TurnIndex = 5,
            DeadlineUnixMs = DateTimeOffset.UtcNow.AddSeconds(30).ToUnixTimeMilliseconds(),
            NoActionStreakBoth = 2,
            LastResolvedTurnIndex = 4,
            MatchId = Guid.NewGuid(),
            Version = 10,
            PlayerAHp = 75,
            PlayerBHp = 80,
            PlayerAStrength = 10,
            PlayerAStamina = 8,
            PlayerAAgility = 7,
            PlayerAIntuition = 6,
            PlayerBStrength = 9,
            PlayerBStamina = 10,
            PlayerBAgility = 8,
            PlayerBIntuition = 7
        };

        // Act
        var json = JsonSerializer.Serialize(originalState);
        var deserializedState = JsonSerializer.Deserialize<BattleState>(json);

        // Assert
        deserializedState.Should().NotBeNull();
        deserializedState!.BattleId.Should().Be(originalState.BattleId);
        deserializedState.DeadlineUnixMs.Should().Be(originalState.DeadlineUnixMs);
        deserializedState.Phase.Should().Be(originalState.Phase);
        deserializedState.TurnIndex.Should().Be(originalState.TurnIndex);
        deserializedState.Version.Should().Be(originalState.Version);
        deserializedState.PlayerAHp.Should().Be(originalState.PlayerAHp);
        deserializedState.PlayerBHp.Should().Be(originalState.PlayerBHp);
    }

    [Fact]
    public void GetDeadlineUtc_And_SetDeadlineUtc_ShouldRoundTripCorrectly()
    {
        // Arrange
        var state = new BattleState();
        var originalDeadline = new DateTime(2025, 6, 15, 10, 30, 0, DateTimeKind.Utc);

        // Act
        state.SetDeadlineUtc(originalDeadline);
        var retrievedDeadline = state.GetDeadlineUtc();

        // Assert
        retrievedDeadline.Should().BeCloseTo(originalDeadline, TimeSpan.FromMilliseconds(1));
        state.DeadlineUnixMs.Should().BeGreaterThan(0);
        state.DeadlineUnixMs.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(state.DeadlineUnixMs).ToUnixTimeMilliseconds());
    }
}


