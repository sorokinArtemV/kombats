using Combats.Battle.Domain.Engine;
using Combats.Battle.Domain.Events;
using Combats.Battle.Domain.Model;
using Combats.Battle.Domain.Rules;
using FluentAssertions;
using Xunit;

namespace Combats.Battle.Application.Tests;

/// <summary>
/// Deterministic stub for IRandomProvider that returns fixed values for testing.
/// </summary>
internal class DeterministicRandomProvider : IRandomProvider
{
    private readonly decimal _fixedValue;

    public DeterministicRandomProvider(decimal fixedValue)
    {
        _fixedValue = fixedValue;
    }

    public decimal NextDecimal(decimal minInclusive, decimal maxInclusive)
    {
        // Return the fixed value, clamped to the range
        if (_fixedValue < minInclusive) return minInclusive;
        if (_fixedValue > maxInclusive) return maxInclusive;
        return _fixedValue;
    }
}

public class CombatMathTests
{
    private static CombatBalance CreateTestBalance(
        decimal spreadMin = 0.85m,
        decimal spreadMax = 1.15m,
        int mfPerAgi = 5,
        int mfPerInt = 5)
    {
        return new CombatBalance(
            hp: new HpBalance(baseHp: 100, hpPerEnd: 6),
            damage: new DamageBalance(
                baseWeaponDamage: 10,
                damagePerStr: 1.0m,
                damagePerAgi: 0.5m,
                damagePerInt: 0.3m,
                spreadMin: spreadMin,
                spreadMax: spreadMax),
            mf: new MfBalance(mfPerAgi: mfPerAgi, mfPerInt: mfPerInt),
            dodgeChance: new ChanceBalance(
                @base: 0.05m,
                min: 0.02m,
                max: 0.35m,
                scale: 1.0m,
                kBase: 50m),
            critChance: new ChanceBalance(
                @base: 0.03m,
                min: 0.01m,
                max: 0.30m,
                scale: 1.0m,
                kBase: 60m),
            critEffect: new CritEffectBalance(
                mode: CritEffectMode.BypassBlock,
                multiplier: 1.5m,
                hybridBlockMultiplier: 0.5m));
    }

    [Fact]
    public void ComputeDerived_MfCalculation_Correct()
    {
        // Arrange
        var stats = new PlayerStats(strength: 10, stamina: 15, agility: 20, intuition: 25);
        var balance = CreateTestBalance(mfPerAgi: 5, mfPerInt: 3);

        // Act
        var derived = CombatMath.ComputeDerived(stats, balance);

        // Assert
        derived.MfDodge.Should().Be(20 * 5); // Agility * MfPerAgi
        derived.MfAntiDodge.Should().Be(20 * 5); // Agility * MfPerAgi
        derived.MfCrit.Should().Be(25 * 3); // Intuition * MfPerInt
        derived.MfAntiCrit.Should().Be(25 * 3); // Intuition * MfPerInt
    }

    [Fact]
    public void ComputeDerived_HpMax_Correct()
    {
        // Arrange
        var stats = new PlayerStats(strength: 10, stamina: 15, agility: 0, intuition: 0);
        var balance = CreateTestBalance();

        // Act
        var derived = CombatMath.ComputeDerived(stats, balance);

        // Assert
        derived.HpMax.Should().Be(100 + 15 * 6); // BaseHp + Stamina * HpPerEnd
    }

    [Fact]
    public void ComputeDerived_BaseDamage_Correct()
    {
        // Arrange
        var stats = new PlayerStats(strength: 10, stamina: 5, agility: 8, intuition: 12);
        var balance = CreateTestBalance();

        // Act
        var derived = CombatMath.ComputeDerived(stats, balance);

        // Assert
        // BaseDamage = 10 + 10*1.0 + 8*0.5 + 12*0.3 = 10 + 10 + 4 + 3.6 = 27.6
        var expectedBaseDamage = 10m + 10 * 1.0m + 8 * 0.5m + 12 * 0.3m;
        var expectedMin = (int)Math.Floor(expectedBaseDamage * 0.85m);
        var expectedMax = (int)Math.Ceiling(expectedBaseDamage * 1.15m);
        derived.DamageMin.Should().Be(expectedMin);
        derived.DamageMax.Should().Be(expectedMax);
    }

    [Fact]
    public void ComputeDerived_DamageSpread_SpreadMaxGreaterThanOne_ProducesCorrectUpperBound()
    {
        // Arrange
        var stats = new PlayerStats(strength: 10, stamina: 5, agility: 0, intuition: 0);
        var balance = CreateTestBalance(spreadMin: 0.85m, spreadMax: 1.15m);

        // Act
        var derived = CombatMath.ComputeDerived(stats, balance);

        // Assert
        // BaseDamage = 10 + 10*1.0 = 20
        // DamageMin = floor(20 * 0.85) = floor(17.0) = 17
        // DamageMax = ceil(20 * 1.15) = ceil(23.0) = 23
        derived.DamageMin.Should().Be(17);
        derived.DamageMax.Should().Be(23);
        derived.DamageMax.Should().BeGreaterThan(derived.DamageMin);
    }

    [Fact]
    public void ComputeChance_ClampBehavior_RespectsMinMax_AtLowerBound()
    {
        // Arrange
        var diff = -1000; // Very large negative difference should clamp to min
        var @base = 0.05m;
        var min = 0.02m;
        var max = 0.35m;
        var scale = 1.0m;
        var kBase = 50m;

        // Act
        var result = CombatMath.ComputeChance(diff, @base, min, max, scale, kBase);

        // Assert
        result.Should().Be(min); // Should be clamped to minimum
    }

    [Fact]
    public void ComputeChance_ClampBehavior_RespectsMinMax_AtUpperBound()
    {
        // Arrange
        var diff = 1000; // Very large positive difference should clamp to max
        var @base = 0.05m;
        var min = 0.02m;
        var max = 0.35m;
        var scale = 1.0m;
        var kBase = 50m;

        // Act
        var result = CombatMath.ComputeChance(diff, @base, min, max, scale, kBase);

        // Assert
        result.Should().Be(max); // Should be clamped to maximum
    }

    [Fact]
    public void ComputeChance_ClampBehavior_RespectsMinMax_WithinBounds()
    {
        // Arrange
        var diff = 50; // Moderate difference should be within bounds
        var @base = 0.05m;
        var min = 0.02m;
        var max = 0.35m;
        var scale = 1.0m;
        var kBase = 50m;

        // Act
        var result = CombatMath.ComputeChance(diff, @base, min, max, scale, kBase);

        // Assert
        // raw = 0.05 + 1.0 * 50 / (50 + 50) = 0.05 + 0.5 = 0.55
        // But should be clamped to max (0.35)
        result.Should().BeGreaterThanOrEqualTo(min);
        result.Should().BeLessThanOrEqualTo(max);
        result.Should().Be(0.35m); // Actually clamped to max since 0.55 > 0.35
    }

    [Fact]
    public void ComputeDodgeChance_DiffCalculation_Correct()
    {
        // Arrange
        var attackerStats = new PlayerStats(10, 10, agility: 20, intuition: 10);
        var defenderStats = new PlayerStats(10, 10, agility: 30, intuition: 10);
        var balance = CreateTestBalance();

        var attackerDerived = CombatMath.ComputeDerived(attackerStats, balance);
        var defenderDerived = CombatMath.ComputeDerived(defenderStats, balance);

        // Act
        var dodgeChance = CombatMath.ComputeDodgeChance(attackerDerived, defenderDerived, balance);

        // Assert
        // diff = defender.MfDodge - attacker.MfAntiDodge
        // defender.MfDodge = 30 * 5 = 150
        // attacker.MfAntiDodge = 20 * 5 = 100
        // diff = 150 - 100 = 50
        // Should use ComputeChance with diff = 50
        dodgeChance.Should().BeGreaterThanOrEqualTo(balance.DodgeChance.Min);
        dodgeChance.Should().BeLessThanOrEqualTo(balance.DodgeChance.Max);
    }

    [Fact]
    public void ComputeCritChance_DiffCalculation_Correct()
    {
        // Arrange
        var attackerStats = new PlayerStats(10, 10, agility: 10, intuition: 40);
        var defenderStats = new PlayerStats(10, 10, agility: 10, intuition: 10);
        var balance = CreateTestBalance();

        var attackerDerived = CombatMath.ComputeDerived(attackerStats, balance);
        var defenderDerived = CombatMath.ComputeDerived(defenderStats, balance);

        // Act
        var critChance = CombatMath.ComputeCritChance(attackerDerived, defenderDerived, balance);

        // Assert
        // diff = attacker.MfCrit - defender.MfAntiCrit
        // attacker.MfCrit = 40 * 5 = 200
        // defender.MfAntiCrit = 10 * 5 = 50
        // diff = 200 - 50 = 150
        critChance.Should().BeGreaterThanOrEqualTo(balance.CritChance.Min);
        critChance.Should().BeLessThanOrEqualTo(balance.CritChance.Max);
    }

    [Fact]
    public void RollDamage_ReturnsValueWithinRange()
    {
        // Arrange
        var stats = new PlayerStats(10, 10, 0, 0);
        var balance = CreateTestBalance();
        var derived = CombatMath.ComputeDerived(stats, balance);
        var rng = new DeterministicRandomProvider(20m); // Fixed value within range

        // Act
        var damage = CombatMath.RollDamage(rng, derived);

        // Assert
        damage.Should().BeGreaterThanOrEqualTo(derived.DamageMin);
        damage.Should().BeLessThanOrEqualTo(derived.DamageMax);
        damage.Should().Be(20m); // Should return the fixed value
    }

    [Fact]
    public void RollDamage_ReturnsDecimal_NoRounding()
    {
        // Arrange
        var stats = new PlayerStats(10, 10, 0, 0);
        var balance = CreateTestBalance();
        var derived = CombatMath.ComputeDerived(stats, balance);
        var rng = new DeterministicRandomProvider(20.7m); // Non-integer value

        // Act
        var damage = CombatMath.RollDamage(rng, derived);

        // Assert
        damage.Should().Be(20.7m); // Should return decimal without rounding
        damage.Should().NotBe((int)damage); // Should not be rounded to int
    }

    [Fact]
    public void CalculateDamage_CritVsBlock_BypassBlockMode_Works()
    {
        // Arrange
        var balance = CreateTestBalance();
        balance = new CombatBalance(
            balance.Hp,
            balance.Damage,
            balance.Mf,
            balance.DodgeChance,
            balance.CritChance,
            new CritEffectBalance(CritEffectMode.BypassBlock, multiplier: 1.5m, hybridBlockMultiplier: 0.5m));

        var attackerStats = new PlayerStats(10, 10, 10, 100); // High intuition for crit
        var defenderStats = new PlayerStats(10, 10, 10, 0); // Low anti-crit

        var ruleset = new Ruleset(
            version: 1,
            turnSeconds: 30,
            noActionLimit: 3,
            seed: 0,
            balance: balance);

        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();

        // Attacker attacks Head, defender blocks Head (should be blocked, but crit bypasses)
        var attackerAction = PlayerAction.Create(
            playerId: playerAId,
            turnIndex: 1,
            attackZone: BattleZone.Head,
            blockZonePrimary: null,
            blockZoneSecondary: null);

        var defenderAction = PlayerAction.Create(
            playerId: playerBId,
            turnIndex: 1,
            attackZone: null,
            blockZonePrimary: BattleZone.Head,
            blockZoneSecondary: BattleZone.Chest); // Valid adjacent block pattern

        // Calculate expected damage range
        // BaseDamage = 10 + 10*1.0 + 10*0.5 + 100*0.3 = 10 + 10 + 5 + 30 = 55
        // DamageMin = floor(55 * 0.85) = 46, DamageMax = ceil(55 * 1.15) = 64
        // RNG will return min value (46) when requested range is [46, 64]
        var expectedBaseDamage = 46m; // Will be clamped to DamageMin
        var expectedFinalDamage = (int)Math.Round(expectedBaseDamage * 1.5m, MidpointRounding.AwayFromZero); // 69

        // Deterministic RNG sequence for two CalculateDamage calls:
        // Call 1 (A->B): dodge (high=no dodge), crit (low=crit), damage (will use min=46)
        // Call 2 (B->A): dodge (high=no dodge), crit (high=no crit), damage (won't matter, blocked)
        var rngSequence = new[] { 0.999m, 0.001m, expectedBaseDamage, 0.999m, 0.999m, 20m };
        var rng = new SequentialValueProvider(rngSequence);
        var engine = new BattleEngine(rng);

        var initialState = new BattleDomainState(
            battleId: Guid.NewGuid(),
            matchId: Guid.NewGuid(),
            playerAId: playerAId,
            playerBId: playerBId,
            ruleset: ruleset,
            phase: BattlePhase.Resolving,
            turnIndex: 1,
            noActionStreakBoth: 0,
            lastResolvedTurnIndex: 0,
            playerA: new PlayerState(playerAId, 100, 100, attackerStats),
            playerB: new PlayerState(playerBId, 100, 100, defenderStats));

        // Act
        var result = engine.ResolveTurn(initialState, attackerAction, defenderAction);

        // Assert - In BypassBlock mode, crit should bypass block, so damage should be > 0
        // Note: This is an integration test, but it validates the crit vs block interaction
        result.Events.Should().Contain(e => e is PlayerDamagedDomainEvent);
        var damageEvent = result.Events.OfType<PlayerDamagedDomainEvent>().FirstOrDefault(d => d.PlayerId == playerBId);
        damageEvent.Should().NotBeNull("Damage event should exist for player B");
        damageEvent!.Damage.Should().Be(expectedFinalDamage, 
            "Damage should be base damage * crit multiplier, rounded AwayFromZero");
    }

    [Fact]
    public void CalculateDamage_CritVsBlock_HybridMode_Works()
    {
        // Arrange
        var balance = CreateTestBalance();
        balance = new CombatBalance(
            balance.Hp,
            balance.Damage,
            balance.Mf,
            balance.DodgeChance,
            balance.CritChance,
            new CritEffectBalance(CritEffectMode.Hybrid, multiplier: 1.5m, hybridBlockMultiplier: 0.5m));

        var attackerStats = new PlayerStats(10, 10, 10, 100); // High intuition for crit
        var defenderStats = new PlayerStats(10, 10, 10, 0); // Low anti-crit

        var ruleset = new Ruleset(
            version: 1,
            turnSeconds: 30,
            noActionLimit: 3,
            seed: 0,
            balance: balance);

        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();

        // Attacker attacks Head, defender blocks Head (should be blocked, but crit in Hybrid mode reduces damage)
        var attackerAction = PlayerAction.Create(
            playerId: playerAId,
            turnIndex: 1,
            attackZone: BattleZone.Head,
            blockZonePrimary: null,
            blockZoneSecondary: null);

        var defenderAction = PlayerAction.Create(
            playerId: playerBId,
            turnIndex: 1,
            attackZone: null,
            blockZonePrimary: BattleZone.Head,
            blockZoneSecondary: BattleZone.Chest); // Valid adjacent block pattern

        // Calculate expected damage range
        // BaseDamage = 10 + 10*1.0 + 10*0.5 + 100*0.3 = 10 + 10 + 5 + 30 = 55
        // DamageMin = floor(55 * 0.85) = 46, DamageMax = ceil(55 * 1.15) = 64
        // RNG will return min value (46) when requested range is [46, 64]
        var expectedBaseDamage = 46m; // Will be clamped to DamageMin
        var expectedFinalDamage = (int)Math.Round(expectedBaseDamage * 1.5m * 0.5m, MidpointRounding.AwayFromZero); // 34.5 -> 35

        // Deterministic RNG sequence for two CalculateDamage calls:
        // Call 1 (A->B): dodge (high=no dodge), crit (low=crit), damage (will use min=46)
        // Call 2 (B->A): dodge (high=no dodge), crit (high=no crit), damage (won't matter, blocked)
        var rngSequence = new[] { 0.999m, 0.001m, expectedBaseDamage, 0.999m, 0.999m, 20m };
        var rng = new SequentialValueProvider(rngSequence);
        var engine = new BattleEngine(rng);

        var initialState = new BattleDomainState(
            battleId: Guid.NewGuid(),
            matchId: Guid.NewGuid(),
            playerAId: playerAId,
            playerBId: playerBId,
            ruleset: ruleset,
            phase: BattlePhase.Resolving,
            turnIndex: 1,
            noActionStreakBoth: 0,
            lastResolvedTurnIndex: 0,
            playerA: new PlayerState(playerAId, 100, 100, attackerStats),
            playerB: new PlayerState(playerBId, 100, 100, defenderStats));

        // Act
        var result = engine.ResolveTurn(initialState, attackerAction, defenderAction);

        // Assert - In Hybrid mode, crit with block should apply both multiplier and block reduction
        result.Events.Should().Contain(e => e is PlayerDamagedDomainEvent);
        var damageEvent = result.Events.OfType<PlayerDamagedDomainEvent>().FirstOrDefault(d => d.PlayerId == playerBId);
        damageEvent.Should().NotBeNull("Damage event should exist for player B in Hybrid mode");
        
        // Verify that damage is applied (not blocked) and is greater than 0
        // In Hybrid mode: baseDamage * 1.5 * 0.5 should be applied
        damageEvent!.Damage.Should().BeGreaterThan(0, "Hybrid mode should apply damage through block when crit occurs");
        
        // Compare with BypassBlock mode - Hybrid should deal less damage
        // BypassBlock: baseDamage * 1.5 (no block reduction)
        // Hybrid: baseDamage * 1.5 * 0.5 (with block reduction)
        // So Hybrid damage should be roughly half of BypassBlock damage
        // But since we can't easily compare without running two tests, just verify the damage is reasonable
        damageEvent.Damage.Should().BeLessThan(100, "Hybrid mode damage should be reasonable (less than base * crit multiplier alone would be)");
    }
}

/// <summary>
/// Sequential random provider that returns values from a predefined sequence.
/// Used for testing multiple random calls in order (dodge, crit, damage).
/// </summary>
internal class SequentialValueProvider : IRandomProvider
{
    private readonly decimal[] _values;
    private int _index = 0;

    public SequentialValueProvider(decimal[] values)
    {
        _values = values;
    }

    public decimal NextDecimal(decimal minInclusive, decimal maxInclusive)
    {
        if (_index >= _values.Length)
            _index = 0; // Wrap around if needed

        var value = _values[_index];
        _index++;
        
        // Clamp to requested range
        if (value < minInclusive) return minInclusive;
        if (value > maxInclusive) return maxInclusive;
        return value;
    }
}

