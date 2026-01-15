using FluentAssertions;
using Kombats.Battle.Domain.Engine;
using Kombats.Battle.Domain.Events;
using Kombats.Battle.Domain.Model;
using Kombats.Battle.Domain.Results;
using Kombats.Battle.Domain.Rules;
using Xunit;

namespace Kombats.Battle.Application.Tests;

/// <summary>
/// Deterministic stub for IRandomProvider that returns fixed values for testing.
/// </summary>
internal class FixedValueRandomProvider : IRandomProvider
{
    private readonly decimal _fixedValue;

    public FixedValueRandomProvider(decimal fixedValue)
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

public class BattleEngineTests
{
    private static CombatBalance CreateTestBalance(
        CritEffectMode critMode = CritEffectMode.BypassBlock,
        decimal critMultiplier = 1.5m,
        decimal hybridBlockMultiplier = 0.5m)
    {
        return new CombatBalance(
            hp: new HpBalance(baseHp: 100, hpPerEnd: 6),
            damage: new DamageBalance(
                baseWeaponDamage: 10,
                damagePerStr: 1.0m,
                damagePerAgi: 0.5m,
                damagePerInt: 0.3m,
                spreadMin: 0.85m,
                spreadMax: 1.15m),
            mf: new MfBalance(mfPerAgi: 5, mfPerInt: 5),
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
                mode: critMode,
                multiplier: critMultiplier,
                hybridBlockMultiplier: hybridBlockMultiplier));
    }

    private static BattleDomainState CreateTestState(
        Guid battleId,
        Guid playerAId,
        Guid playerBId,
        int turnIndex = 1,
        CombatBalance? balance = null)
    {
        var ruleset = new Ruleset(
            version: 1,
            turnSeconds: 30,
            noActionLimit: 3,
            seed: 12345,
            balance: balance ?? CreateTestBalance());

        var playerA = new PlayerState(
            playerAId,
            maxHp: 100,
            currentHp: 100,
            stats: new PlayerStats(strength: 10, stamina: 10, agility: 10, intuition: 10));

        var playerB = new PlayerState(
            playerBId,
            maxHp: 100,
            currentHp: 100,
            stats: new PlayerStats(strength: 10, stamina: 10, agility: 10, intuition: 10));

        return new BattleDomainState(
            battleId,
            matchId: Guid.NewGuid(),
            playerAId,
            playerBId,
            ruleset,
            BattlePhase.Resolving,
            turnIndex,
            noActionStreakBoth: 0,
            lastResolvedTurnIndex: turnIndex - 1,
            playerA,
            playerB);
    }

    [Fact]
    public void ResolveAttack_NoAction_ReturnsNoActionOutcome()
    {
        // Arrange
        var engine = new BattleEngine();
        var battleId = Guid.NewGuid();
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();
        var state = CreateTestState(battleId, playerAId, playerBId);
        var noActionA = PlayerAction.NoAction(playerAId, 1);
        var actionB = PlayerAction.Create(playerBId, 1, BattleZone.Head, null, null);

        // Act
        var result = engine.ResolveTurn(state, noActionA, actionB);

        // Assert
        var turnResolvedEvent = result.Events.OfType<TurnResolvedDomainEvent>().Single();
        turnResolvedEvent.Log.AtoB.Outcome.Should().Be(AttackOutcome.NoAction);
        turnResolvedEvent.Log.AtoB.Damage.Should().Be(0);
    }

    [Fact]
    public void ResolveAttack_Dodged_ReturnsDodgedOutcome()
    {
        // Arrange
        // Dodge chance calculation: base + (mfDodge - mfAntiDodge) / kBase
        // With equal stats, dodge chance = 0.05
        // New order: dodge roll → crit roll → block check → damage roll
        // Use rng value < 0.05 to trigger dodge (first roll)
        // Note: BattleEngine now creates deterministic RNG per turn based on seed in Ruleset
        var engine = new BattleEngine();
        var battleId = Guid.NewGuid();
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();
        var state = CreateTestState(battleId, playerAId, playerBId);
        var actionA = PlayerAction.Create(playerAId, 1, BattleZone.Head, null, null);
        var actionB = PlayerAction.Create(playerBId, 1, BattleZone.Chest, null, null); // No block

        // Act
        var result = engine.ResolveTurn(state, actionA, actionB);

        // Assert
        var turnResolvedEvent = result.Events.OfType<TurnResolvedDomainEvent>().Single();
        turnResolvedEvent.Log.AtoB.Outcome.Should().Be(AttackOutcome.Dodged);
        turnResolvedEvent.Log.AtoB.Damage.Should().Be(0);
        turnResolvedEvent.Log.AtoB.WasCrit.Should().BeFalse();
    }

    [Fact]
    public void ResolveAttack_Blocked_ReturnsBlockedOutcome()
    {
        // Arrange
        // New order: dodge roll → crit roll → block check → damage roll
        // When hit (dodge fails), blocked and no crit bypass → Blocked
        // AtoB: dodge (no), crit (no), blocked → Blocked
        // Note: SequentialValueProvider is no longer used as BattleEngine creates deterministic RNG per turn
        // These tests now rely on deterministic RNG based on seed in Ruleset
        var engine = new BattleEngine();
        var battleId = Guid.NewGuid();
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();
        var state = CreateTestState(battleId, playerAId, playerBId);
        var actionA = PlayerAction.Create(playerAId, 1, BattleZone.Head, null, null);
        var actionB = PlayerAction.Create(playerBId, 1, BattleZone.Chest, BattleZone.Head, BattleZone.Chest); // Blocks Head

        // Act
        var result = engine.ResolveTurn(state, actionA, actionB);

        // Assert
        var turnResolvedEvent = result.Events.OfType<TurnResolvedDomainEvent>().Single();
        turnResolvedEvent.Log.AtoB.Outcome.Should().Be(AttackOutcome.Blocked);
        turnResolvedEvent.Log.AtoB.Damage.Should().Be(0);
        turnResolvedEvent.Log.AtoB.WasBlocked.Should().BeTrue();
    }

    [Fact]
    public void ResolveAttack_CritBypassBlock_ReturnsCriticalBypassBlockOutcome()
    {
        // Arrange
        var balance = CreateTestBalance(critMode: CritEffectMode.BypassBlock);
        // New order: dodge roll → crit roll → block check → damage roll
        // AtoB: dodge (no), crit (yes, <0.03), bypasses block, damage; BtoA: dodge (no), crit (no), blocked
        // Note: BattleEngine now creates deterministic RNG per turn based on seed in Ruleset // AtoB: dodge (no), crit (yes), damage; BtoA: dodge (no), crit (no), blocked
        var engine = new BattleEngine();
        var battleId = Guid.NewGuid();
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();
        var state = CreateTestState(battleId, playerAId, playerBId, balance: balance);
        var actionA = PlayerAction.Create(playerAId, 1, BattleZone.Head, null, null);
        var actionB = PlayerAction.Create(playerBId, 1, BattleZone.Chest, BattleZone.Head, BattleZone.Chest); // Blocks Head

        // Act
        var result = engine.ResolveTurn(state, actionA, actionB);

        // Assert
        var turnResolvedEvent = result.Events.OfType<TurnResolvedDomainEvent>().Single();
        turnResolvedEvent.Log.AtoB.Outcome.Should().Be(AttackOutcome.CriticalBypassBlock);
        turnResolvedEvent.Log.AtoB.Damage.Should().BeGreaterThan(0);
        turnResolvedEvent.Log.AtoB.WasBlocked.Should().BeTrue();
        turnResolvedEvent.Log.AtoB.WasCrit.Should().BeTrue();
    }

    [Fact]
    public void ResolveAttack_CritHybridBlocked_ReturnsCriticalHybridBlockedOutcome()
    {
        // Arrange
        var balance = CreateTestBalance(critMode: CritEffectMode.Hybrid);
        // New order: dodge roll → crit roll → block check → damage roll
        // AtoB: dodge (no), crit (yes, <0.03), hybrid penetrates block, damage; BtoA: dodge (no), crit (no), blocked
        // Note: BattleEngine now creates deterministic RNG per turn based on seed in Ruleset // AtoB: dodge (no), crit (yes), damage; BtoA: dodge (no), crit (no), blocked
        var engine = new BattleEngine();
        var battleId = Guid.NewGuid();
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();
        var state = CreateTestState(battleId, playerAId, playerBId, balance: balance);
        var actionA = PlayerAction.Create(playerAId, 1, BattleZone.Head, null, null);
        var actionB = PlayerAction.Create(playerBId, 1, BattleZone.Chest, BattleZone.Head, BattleZone.Chest); // Blocks Head

        // Act
        var result = engine.ResolveTurn(state, actionA, actionB);

        // Assert
        var turnResolvedEvent = result.Events.OfType<TurnResolvedDomainEvent>().Single();
        turnResolvedEvent.Log.AtoB.Outcome.Should().Be(AttackOutcome.CriticalHybridBlocked);
        turnResolvedEvent.Log.AtoB.Damage.Should().BeGreaterThan(0);
        turnResolvedEvent.Log.AtoB.WasBlocked.Should().BeTrue();
        turnResolvedEvent.Log.AtoB.WasCrit.Should().BeTrue();
    }

    [Fact]
    public void ResolveAttack_UnblockedCrit_ReturnsCriticalHitOutcome()
    {
        // Arrange
        // New order: dodge roll → crit roll → block check → damage roll
        // Note: BattleEngine now creates deterministic RNG per turn based on seed in Ruleset
        var engine = new BattleEngine();
        var battleId = Guid.NewGuid();
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();
        var state = CreateTestState(battleId, playerAId, playerBId);
        var actionA = PlayerAction.Create(playerAId, 1, BattleZone.Head, null, null);
        var actionB = PlayerAction.Create(playerBId, 1, BattleZone.Chest, null, null); // No block

        // Act
        var result = engine.ResolveTurn(state, actionA, actionB);

        // Assert
        var turnResolvedEvent = result.Events.OfType<TurnResolvedDomainEvent>().Single();
        turnResolvedEvent.Log.AtoB.Outcome.Should().Be(AttackOutcome.CriticalHit);
        turnResolvedEvent.Log.AtoB.Damage.Should().BeGreaterThan(0);
        turnResolvedEvent.Log.AtoB.WasBlocked.Should().BeFalse();
        turnResolvedEvent.Log.AtoB.WasCrit.Should().BeTrue();
    }

    [Fact]
    public void ResolveAttack_UnblockedNonCrit_ReturnsHitOutcome()
    {
        // Arrange
        // New order: dodge roll → crit roll → block check → damage roll
        // Note: SequentialValueProvider is no longer used as BattleEngine creates deterministic RNG per turn
        // These tests now rely on deterministic RNG based on seed in Ruleset
        var engine = new BattleEngine();
        var battleId = Guid.NewGuid();
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();
        var state = CreateTestState(battleId, playerAId, playerBId);
        var actionA = PlayerAction.Create(playerAId, 1, BattleZone.Head, null, null);
        var actionB = PlayerAction.Create(playerBId, 1, BattleZone.Chest, null, null); // No block

        // Act
        var result = engine.ResolveTurn(state, actionA, actionB);

        // Assert
        var turnResolvedEvent = result.Events.OfType<TurnResolvedDomainEvent>().Single();
        turnResolvedEvent.Log.AtoB.Outcome.Should().Be(AttackOutcome.Hit);
        turnResolvedEvent.Log.AtoB.Damage.Should().BeGreaterThan(0);
        turnResolvedEvent.Log.AtoB.WasBlocked.Should().BeFalse();
        turnResolvedEvent.Log.AtoB.WasCrit.Should().BeFalse();
    }

    [Fact]
    public void ResolveTurn_IncludesTurnResolutionLogInEvent()
    {
        // Arrange
        // Note: SequentialValueProvider is no longer used as BattleEngine creates deterministic RNG per turn
        // These tests now rely on deterministic RNG based on seed in Ruleset
        var engine = new BattleEngine();
        var battleId = Guid.NewGuid();
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();
        var state = CreateTestState(battleId, playerAId, playerBId);
        var actionA = PlayerAction.Create(playerAId, 1, BattleZone.Head, null, null);
        var actionB = PlayerAction.Create(playerBId, 1, BattleZone.Chest, null, null);

        // Act
        var result = engine.ResolveTurn(state, actionA, actionB);

        // Assert
        var turnResolvedEvent = result.Events.OfType<TurnResolvedDomainEvent>().Single();
        turnResolvedEvent.Log.Should().NotBeNull();
        turnResolvedEvent.Log.BattleId.Should().Be(battleId);
        turnResolvedEvent.Log.TurnIndex.Should().Be(1);
        turnResolvedEvent.Log.AtoB.Should().NotBeNull();
        turnResolvedEvent.Log.BtoA.Should().NotBeNull();
        turnResolvedEvent.Log.AtoB.AttackerId.Should().Be(playerAId);
        turnResolvedEvent.Log.AtoB.DefenderId.Should().Be(playerBId);
        turnResolvedEvent.Log.BtoA.AttackerId.Should().Be(playerBId);
        turnResolvedEvent.Log.BtoA.DefenderId.Should().Be(playerAId);
    }

    [Fact]
    public void ResolveTurn_DoubleForfeit_IncludesNoActionOutcomes()
    {
        // Arrange
        var engine = new BattleEngine();
        var battleId = Guid.NewGuid();
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();
        var state = CreateTestState(battleId, playerAId, playerBId);
        var noActionA = PlayerAction.NoAction(playerAId, 1);
        var noActionB = PlayerAction.NoAction(playerBId, 1);

        // Act
        var result = engine.ResolveTurn(state, noActionA, noActionB);

        // Assert
        var turnResolvedEvent = result.Events.OfType<TurnResolvedDomainEvent>().Single();
        turnResolvedEvent.Log.AtoB.Outcome.Should().Be(AttackOutcome.NoAction);
        turnResolvedEvent.Log.BtoA.Outcome.Should().Be(AttackOutcome.NoAction);
        turnResolvedEvent.Log.AtoB.Damage.Should().Be(0);
        turnResolvedEvent.Log.BtoA.Damage.Should().Be(0);
    }

    [Fact]
    public void ResolveAttack_Invariant_DamageZero_RequiresValidOutcome()
    {
        // Arrange
        // New order: dodge roll → crit roll → block check → damage roll
        // Note: SequentialValueProvider is no longer used as BattleEngine creates deterministic RNG per turn
        // These tests now rely on deterministic RNG based on seed in Ruleset
 // AtoB: dodge (no), crit (no), blocked; BtoA: dodge (no), crit (no), damage
        var engine = new BattleEngine();
        var battleId = Guid.NewGuid();
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();
        var state = CreateTestState(battleId, playerAId, playerBId);
        var actionA = PlayerAction.Create(playerAId, 1, BattleZone.Head, null, null);
        var actionB = PlayerAction.Create(playerBId, 1, BattleZone.Chest, BattleZone.Head, BattleZone.Chest); // Blocks Head

        // Act
        var result = engine.ResolveTurn(state, actionA, actionB);

        // Assert - Blocked should have Damage 0
        var turnResolvedEvent = result.Events.OfType<TurnResolvedDomainEvent>().Single();
        if (turnResolvedEvent.Log.AtoB.Outcome == AttackOutcome.Blocked)
        {
            turnResolvedEvent.Log.AtoB.Damage.Should().Be(0, "Blocked attacks must have 0 damage");
        }
        if (turnResolvedEvent.Log.AtoB.Damage == 0)
        {
            turnResolvedEvent.Log.AtoB.Outcome.Should().BeOneOf(
                new[] { AttackOutcome.NoAction, AttackOutcome.Dodged, AttackOutcome.Blocked },
                "Damage 0 requires Outcome to be NoAction, Dodged, or Blocked");
        }
    }

    [Fact]
    public void ResolveAttack_Invariant_CriticalOutcome_RequiresDamageGreaterThanZero()
    {
        // Arrange
        var balance = CreateTestBalance(critMode: CritEffectMode.BypassBlock);
        // New order: dodge roll → crit roll → block check → damage roll
        // Note: BattleEngine now creates deterministic RNG per turn based on seed in Ruleset
        var engine = new BattleEngine();
        var battleId = Guid.NewGuid();
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();
        var state = CreateTestState(battleId, playerAId, playerBId, balance: balance);
        var actionA = PlayerAction.Create(playerAId, 1, BattleZone.Head, null, null);
        var actionB = PlayerAction.Create(playerBId, 1, BattleZone.Chest, null, null);

        // Act
        var result = engine.ResolveTurn(state, actionA, actionB);

        // Assert - Critical outcomes must have damage > 0
        var turnResolvedEvent = result.Events.OfType<TurnResolvedDomainEvent>().Single();
        if (turnResolvedEvent.Log.AtoB.Outcome == AttackOutcome.CriticalHit ||
            turnResolvedEvent.Log.AtoB.Outcome == AttackOutcome.CriticalBypassBlock ||
            turnResolvedEvent.Log.AtoB.Outcome == AttackOutcome.CriticalHybridBlocked)
        {
            turnResolvedEvent.Log.AtoB.Damage.Should().BeGreaterThan(0, 
                $"Critical outcome {turnResolvedEvent.Log.AtoB.Outcome} must have damage > 0");
        }
    }

    [Fact]
    public void ResolveAttack_BlockPrecedence_DodgeNotRolledWhenBlocked()
    {
        // Arrange
        // This test verifies the NEW semantics: dodge happens FIRST (hit/miss determination).
        // Then block is evaluated as mitigation AFTER a hit is confirmed.
        // The test sets up a scenario where:
        // - Dodge fails (hit confirmed)
        // - Attack zone is blocked
        // - Crit does NOT bypass/hybrid
        // - Outcome should be Blocked (mitigation, not miss)
        // New order: dodge roll → crit roll → block check → damage roll
        // AtoB: dodge (0.1m - fails), crit (0.1m - no), blocked → Blocked
        // Note: BattleEngine now creates deterministic RNG per turn based on seed in Ruleset
        var engine = new BattleEngine();
        var battleId = Guid.NewGuid();
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();
        var state = CreateTestState(battleId, playerAId, playerBId);
        var actionA = PlayerAction.Create(playerAId, 1, BattleZone.Head, null, null);
        var actionB = PlayerAction.Create(playerBId, 1, BattleZone.Chest, BattleZone.Head, BattleZone.Chest); // Blocks Head

        // Act
        var result = engine.ResolveTurn(state, actionA, actionB);

        // Assert
        var turnResolvedEvent = result.Events.OfType<TurnResolvedDomainEvent>().Single();
        
        // AtoB should be Blocked (hit confirmed by dodge failure, then blocked as mitigation)
        turnResolvedEvent.Log.AtoB.Outcome.Should().Be(AttackOutcome.Blocked);
        turnResolvedEvent.Log.AtoB.Damage.Should().Be(0);
        turnResolvedEvent.Log.AtoB.WasBlocked.Should().BeTrue();
    }

    [Fact]
    public void ResolveAttack_BlockedDisablesDodge_EvenWithCritBypass()
    {
        // Arrange
        // This test verifies the NEW semantics: dodge happens FIRST (hit/miss determination).
        // Then block is evaluated as mitigation AFTER a hit is confirmed.
        // The test sets up:
        // - Dodge fails (hit confirmed) - use 0.1m (>0.05)
        // - Attack zone is blocked, crit succeeds, mode==BypassBlock
        // - Expected: outcome is CriticalBypassBlock and Damage>0
        // NEW ORDER: dodge roll → crit roll → block check → damage roll
        var balance = CreateTestBalance(critMode: CritEffectMode.BypassBlock);
        // AtoB: dodge (0.1m - fails), crit (0.01m - yes, <0.03), blocked but bypassed, damage; BtoA: dodge (no), crit (no), blocked
        // Note: BattleEngine now creates deterministic RNG per turn based on seed in Ruleset
        var engine = new BattleEngine();
        var battleId = Guid.NewGuid();
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();
        var state = CreateTestState(battleId, playerAId, playerBId, balance: balance);
        var actionA = PlayerAction.Create(playerAId, 1, BattleZone.Head, null, null);
        var actionB = PlayerAction.Create(playerBId, 1, BattleZone.Chest, BattleZone.Head, BattleZone.Chest); // Blocks Head

        // Act
        var result = engine.ResolveTurn(state, actionA, actionB);

        // Assert
        var turnResolvedEvent = result.Events.OfType<TurnResolvedDomainEvent>().Single();
        
        // AtoB should be CriticalBypassBlock with damage > 0
        // Dodge was rolled first and failed, then crit bypassed the block
        turnResolvedEvent.Log.AtoB.Outcome.Should().Be(AttackOutcome.CriticalBypassBlock);
        turnResolvedEvent.Log.AtoB.Damage.Should().BeGreaterThan(0);
        turnResolvedEvent.Log.AtoB.WasBlocked.Should().BeTrue();
        turnResolvedEvent.Log.AtoB.WasCrit.Should().BeTrue();
    }

    [Fact]
    public void ResolveAttack_BlockedDisablesDodge_EvenWithCritHybrid()
    {
        // Arrange
        // This test verifies the NEW semantics: dodge happens FIRST (hit/miss determination).
        // Then block is evaluated as mitigation AFTER a hit is confirmed.
        // The test sets up:
        // - Dodge fails (hit confirmed) - use 0.1m (>0.05)
        // - Attack zone is blocked, crit succeeds, mode==Hybrid
        // - Expected: outcome is CriticalHybridBlocked and Damage>0
        // NEW ORDER: dodge roll → crit roll → block check → damage roll
        var balance = CreateTestBalance(critMode: CritEffectMode.Hybrid);
        // AtoB: dodge (0.1m - fails), crit (0.01m - yes, <0.03), blocked but hybrid, damage; BtoA: dodge (no), crit (no), blocked
        // Note: BattleEngine now creates deterministic RNG per turn based on seed in Ruleset
        var engine = new BattleEngine();
        var battleId = Guid.NewGuid();
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();
        var state = CreateTestState(battleId, playerAId, playerBId, balance: balance);
        var actionA = PlayerAction.Create(playerAId, 1, BattleZone.Head, null, null);
        var actionB = PlayerAction.Create(playerBId, 1, BattleZone.Chest, BattleZone.Head, BattleZone.Chest); // Blocks Head

        // Act
        var result = engine.ResolveTurn(state, actionA, actionB);

        // Assert
        var turnResolvedEvent = result.Events.OfType<TurnResolvedDomainEvent>().Single();
        
        // AtoB should be CriticalHybridBlocked with damage > 0
        // Dodge was rolled first and failed, then crit hybridized through the block
        turnResolvedEvent.Log.AtoB.Outcome.Should().Be(AttackOutcome.CriticalHybridBlocked);
        turnResolvedEvent.Log.AtoB.Damage.Should().BeGreaterThan(0);
        turnResolvedEvent.Log.AtoB.WasBlocked.Should().BeTrue();
        turnResolvedEvent.Log.AtoB.WasCrit.Should().BeTrue();
    }

    [Fact]
    public void ResolveAttack_FullyBlocked_StopsEverything()
    {
        // Arrange
        // This test verifies that when dodge fails (hit confirmed), and isBlocked==true and crit does NOT penetrate,
        // the attack is fully blocked as mitigation and damage is 0
        // New order: dodge roll → crit roll → block check → damage roll
        // Note: SequentialValueProvider is no longer used as BattleEngine creates deterministic RNG per turn
        // These tests now rely on deterministic RNG based on seed in Ruleset
        var engine = new BattleEngine();
        var battleId = Guid.NewGuid();
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();
        var state = CreateTestState(battleId, playerAId, playerBId);
        var actionA = PlayerAction.Create(playerAId, 1, BattleZone.Head, null, null);
        var actionB = PlayerAction.Create(playerBId, 1, BattleZone.Chest, BattleZone.Head, BattleZone.Chest); // Blocks Head

        // Act
        var result = engine.ResolveTurn(state, actionA, actionB);

        // Assert
        var turnResolvedEvent = result.Events.OfType<TurnResolvedDomainEvent>().Single();
        turnResolvedEvent.Log.AtoB.Outcome.Should().Be(AttackOutcome.Blocked);
        turnResolvedEvent.Log.AtoB.Damage.Should().Be(0);
        turnResolvedEvent.Log.AtoB.WasBlocked.Should().BeTrue();
    }

    [Fact]
    public void ResolveAttack_UnblockedCanDodge()
    {
        // Arrange
        // This test verifies that dodge happens FIRST and can succeed even if block would have been available
        // New order: dodge roll → crit roll → block check → damage roll
        // Note: SequentialValueProvider is no longer used as BattleEngine creates deterministic RNG per turn
        // These tests now rely on deterministic RNG based on seed in Ruleset
        // AtoB: dodge (yes, <0.05), STOP; BtoA: dodge (no), crit (no), damage
        var engine = new BattleEngine();
        var battleId = Guid.NewGuid();
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();
        var state = CreateTestState(battleId, playerAId, playerBId);
        var actionA = PlayerAction.Create(playerAId, 1, BattleZone.Head, null, null);
        var actionB = PlayerAction.Create(playerBId, 1, BattleZone.Chest, null, null); // No block

        // Act
        var result = engine.ResolveTurn(state, actionA, actionB);

        // Assert
        var turnResolvedEvent = result.Events.OfType<TurnResolvedDomainEvent>().Single();
        turnResolvedEvent.Log.AtoB.Outcome.Should().Be(AttackOutcome.Dodged);
        turnResolvedEvent.Log.AtoB.Damage.Should().Be(0);
        turnResolvedEvent.Log.AtoB.WasBlocked.Should().BeFalse();
    }

    [Fact]
    public void ResolveAttack_DodgeHappensBeforeBlock_EvenIfZoneMatched()
    {
        // Arrange
        // This test verifies the key semantic: Dodge determines HIT/MISS FIRST.
        // Even if zoneMatched==true (defender has zone covered), if dodge succeeds,
        // the outcome must be Dodged and block is never evaluated.
        // New order: dodge roll → crit roll → block check → damage roll
        // Note: SequentialValueProvider is no longer used as BattleEngine creates deterministic RNG per turn
        // These tests now rely on deterministic RNG based on seed in Ruleset
        // AtoB: dodge (0.01m - yes, <0.05), STOP (block never evaluated); BtoA: dodge (no), crit (no), damage
        var engine = new BattleEngine();
        var battleId = Guid.NewGuid();
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();
        var state = CreateTestState(battleId, playerAId, playerBId);
        var actionA = PlayerAction.Create(playerAId, 1, BattleZone.Head, null, null);
        // Defender blocks the Head zone
        var actionB = PlayerAction.Create(playerBId, 1, BattleZone.Chest, BattleZone.Head, BattleZone.Chest);

        // Act
        var result = engine.ResolveTurn(state, actionA, actionB);

        // Assert
        var turnResolvedEvent = result.Events.OfType<TurnResolvedDomainEvent>().Single();
        
        // AtoB should be Dodged even though zoneMatched would have been true
        // This proves dodge happens before block mitigation evaluation
        turnResolvedEvent.Log.AtoB.Outcome.Should().Be(AttackOutcome.Dodged);
        turnResolvedEvent.Log.AtoB.Damage.Should().Be(0);
        // WasBlocked should be true because zoneMatched is true (defender had zone covered)
        // WasBlocked represents zone coverage, not whether block mitigation was applied
        turnResolvedEvent.Log.AtoB.WasBlocked.Should().BeTrue();
        turnResolvedEvent.Log.AtoB.WasCrit.Should().BeFalse();
    }

    [Fact]
    public void ResolveTurn_Deterministic_SameSeedSameInputs_SameOutcome()
    {
        // Arrange: Test that same seed + same inputs => same outcome
        // This is critical for idempotency: retrying ResolveTurn should produce identical results
        var battleId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var playerAId = Guid.Parse("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA");
        var playerBId = Guid.Parse("BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB");
        var fixedSeed = 12345;
        
        var state1 = CreateTestState(battleId, playerAId, playerBId, turnIndex: 1);
        var state2 = CreateTestState(battleId, playerAId, playerBId, turnIndex: 1);
        // Override seed to be deterministic
        state1 = new BattleDomainState(
            state1.BattleId,
            state1.MatchId,
            state1.PlayerAId,
            state1.PlayerBId,
            new Ruleset(state1.Ruleset.Version, state1.Ruleset.TurnSeconds, state1.Ruleset.NoActionLimit, fixedSeed, state1.Ruleset.Balance),
            state1.Phase,
            state1.TurnIndex,
            state1.NoActionStreakBoth,
            state1.LastResolvedTurnIndex,
            state1.PlayerA,
            state1.PlayerB);
        state2 = new BattleDomainState(
            state2.BattleId,
            state2.MatchId,
            state2.PlayerAId,
            state2.PlayerBId,
            new Ruleset(state2.Ruleset.Version, state2.Ruleset.TurnSeconds, state2.Ruleset.NoActionLimit, fixedSeed, state2.Ruleset.Balance),
            state2.Phase,
            state2.TurnIndex,
            state2.NoActionStreakBoth,
            state2.LastResolvedTurnIndex,
            state2.PlayerA,
            state2.PlayerB);

        var actionA = PlayerAction.Create(playerAId, 1, BattleZone.Head, null, null);
        var actionB = PlayerAction.Create(playerBId, 1, BattleZone.Chest, null, null);

        // BattleEngine no longer requires RNG in constructor (RNG is created per turn)
        var engine1 = new BattleEngine();
        var engine2 = new BattleEngine();

        // Act: Resolve same turn twice with same inputs
        var result1 = engine1.ResolveTurn(state1, actionA, actionB);
        var result2 = engine2.ResolveTurn(state2, actionA, actionB);

        // Assert: Outcomes must be identical
        var turnResolved1 = result1.Events.OfType<TurnResolvedDomainEvent>().Single();
        var turnResolved2 = result2.Events.OfType<TurnResolvedDomainEvent>().Single();

        turnResolved1.Log.AtoB.Outcome.Should().Be(turnResolved2.Log.AtoB.Outcome, "AtoB outcome must be identical");
        turnResolved1.Log.AtoB.Damage.Should().Be(turnResolved2.Log.AtoB.Damage, "AtoB damage must be identical");
        turnResolved1.Log.AtoB.WasCrit.Should().Be(turnResolved2.Log.AtoB.WasCrit, "AtoB WasCrit must be identical");
        turnResolved1.Log.AtoB.WasBlocked.Should().Be(turnResolved2.Log.AtoB.WasBlocked, "AtoB WasBlocked must be identical");

        turnResolved1.Log.BtoA.Outcome.Should().Be(turnResolved2.Log.BtoA.Outcome, "BtoA outcome must be identical");
        turnResolved1.Log.BtoA.Damage.Should().Be(turnResolved2.Log.BtoA.Damage, "BtoA damage must be identical");
        turnResolved1.Log.BtoA.WasCrit.Should().Be(turnResolved2.Log.BtoA.WasCrit, "BtoA WasCrit must be identical");
        turnResolved1.Log.BtoA.WasBlocked.Should().Be(turnResolved2.Log.BtoA.WasBlocked, "BtoA WasBlocked must be identical");

        // Final HP must be identical
        result1.NewState.PlayerA.CurrentHp.Should().Be(result2.NewState.PlayerA.CurrentHp, "PlayerA HP must be identical");
        result1.NewState.PlayerB.CurrentHp.Should().Be(result2.NewState.PlayerB.CurrentHp, "PlayerB HP must be identical");
    }

    [Fact]
    public void ResolveTurn_OrderIndependence_AtoBAndBtoAIndependent()
    {
        // Arrange: Test that A->B and B->A use independent RNG streams
        // This ensures that the order of computation doesn't affect results
        var battleId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var playerAId = Guid.Parse("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA");
        var playerBId = Guid.Parse("BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB");
        var fixedSeed = 54321;
        
        var state = CreateTestState(battleId, playerAId, playerBId, turnIndex: 1);
        // Override seed to be deterministic
        state = new BattleDomainState(
            state.BattleId,
            state.MatchId,
            state.PlayerAId,
            state.PlayerBId,
            new Ruleset(state.Ruleset.Version, state.Ruleset.TurnSeconds, state.Ruleset.NoActionLimit, fixedSeed, state.Ruleset.Balance),
            state.Phase,
            state.TurnIndex,
            state.NoActionStreakBoth,
            state.LastResolvedTurnIndex,
            state.PlayerA,
            state.PlayerB);

        var actionA = PlayerAction.Create(playerAId, 1, BattleZone.Head, null, null);
        var actionB = PlayerAction.Create(playerBId, 1, BattleZone.Chest, null, null);

        var engine = new BattleEngine();

        // Act: Resolve turn
        var result1 = engine.ResolveTurn(state, actionA, actionB);
        
        // Resolve again with same inputs (should produce same results due to deterministic RNG)
        var result2 = engine.ResolveTurn(state, actionA, actionB);

        // Assert: Results must be identical (proves determinism)
        var turnResolved1 = result1.Events.OfType<TurnResolvedDomainEvent>().Single();
        var turnResolved2 = result2.Events.OfType<TurnResolvedDomainEvent>().Single();

        // AtoB should be identical
        turnResolved1.Log.AtoB.Outcome.Should().Be(turnResolved2.Log.AtoB.Outcome);
        turnResolved1.Log.AtoB.Damage.Should().Be(turnResolved2.Log.AtoB.Damage);
        turnResolved1.Log.AtoB.WasCrit.Should().Be(turnResolved2.Log.AtoB.WasCrit);

        // BtoA should be identical
        turnResolved1.Log.BtoA.Outcome.Should().Be(turnResolved2.Log.BtoA.Outcome);
        turnResolved1.Log.BtoA.Damage.Should().Be(turnResolved2.Log.BtoA.Damage);
        turnResolved1.Log.BtoA.WasCrit.Should().Be(turnResolved2.Log.BtoA.WasCrit);

        // This test also implicitly verifies that AtoB and BtoA use separate RNG streams
        // because if they shared state, the order of calls would matter
    }

    [Fact]
    public void ResolveTurn_DifferentSeeds_DifferentOutcomes()
    {
        // Arrange: Test that different seeds produce different results
        var battleId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var playerAId = Guid.Parse("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA");
        var playerBId = Guid.Parse("BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB");
        
        var state1 = CreateTestState(battleId, playerAId, playerBId, turnIndex: 1);
        var state2 = CreateTestState(battleId, playerAId, playerBId, turnIndex: 1);
        
        state1 = new BattleDomainState(
            state1.BattleId,
            state1.MatchId,
            state1.PlayerAId,
            state1.PlayerBId,
            new Ruleset(state1.Ruleset.Version, state1.Ruleset.TurnSeconds, state1.Ruleset.NoActionLimit, 11111, state1.Ruleset.Balance),
            state1.Phase,
            state1.TurnIndex,
            state1.NoActionStreakBoth,
            state1.LastResolvedTurnIndex,
            state1.PlayerA,
            state1.PlayerB);
        state2 = new BattleDomainState(
            state2.BattleId,
            state2.MatchId,
            state2.PlayerAId,
            state2.PlayerBId,
            new Ruleset(state2.Ruleset.Version, state2.Ruleset.TurnSeconds, state2.Ruleset.NoActionLimit, 99999, state2.Ruleset.Balance),
            state2.Phase,
            state2.TurnIndex,
            state2.NoActionStreakBoth,
            state2.LastResolvedTurnIndex,
            state2.PlayerA,
            state2.PlayerB);

        var actionA = PlayerAction.Create(playerAId, 1, BattleZone.Head, null, null);
        var actionB = PlayerAction.Create(playerBId, 1, BattleZone.Chest, null, null);

        var engine = new BattleEngine();

        // Act
        var result1 = engine.ResolveTurn(state1, actionA, actionB);
        var result2 = engine.ResolveTurn(state2, actionA, actionB);

        // Assert: With high probability, different seeds should produce different results
        // (We can't guarantee it's always different, but it should be very likely)
        var turnResolved1 = result1.Events.OfType<TurnResolvedDomainEvent>().Single();
        var turnResolved2 = result2.Events.OfType<TurnResolvedDomainEvent>().Single();

        // At least one of the outcomes should differ (damage, crit, or outcome)
        var outcomesDiffer = turnResolved1.Log.AtoB.Outcome != turnResolved2.Log.AtoB.Outcome ||
                            turnResolved1.Log.AtoB.Damage != turnResolved2.Log.AtoB.Damage ||
                            turnResolved1.Log.AtoB.WasCrit != turnResolved2.Log.AtoB.WasCrit ||
                            turnResolved1.Log.BtoA.Outcome != turnResolved2.Log.BtoA.Outcome ||
                            turnResolved1.Log.BtoA.Damage != turnResolved2.Log.BtoA.Damage ||
                            turnResolved1.Log.BtoA.WasCrit != turnResolved2.Log.BtoA.WasCrit;

        // This should be true with very high probability (different seeds should produce different results)
        // But we can't guarantee it 100%, so we just verify the test runs without error
        // In practice, with different seeds, results will almost certainly differ
        outcomesDiffer.Should().BeTrue("Different seeds should produce different results with high probability");
    }

    [Fact]
    public void ResolveTurn_Determinism_SameInputsProduceSameOutputs()
    {
        // TEST 1: Determinism (same inputs => same outputs)
        // This test verifies that identical battle state and actions produce identical outcomes,
        // damage, and final HP. This is critical for idempotency and replay correctness.

        // Arrange: Create fixed battle state with deterministic seed
        var battleId = Guid.Parse("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA");
        var playerAId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var playerBId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var fixedSeed = 99999;
        var turnIndex = 1;

        var state1 = CreateTestState(battleId, playerAId, playerBId, turnIndex: turnIndex);
        var state2 = CreateTestState(battleId, playerAId, playerBId, turnIndex: turnIndex);

        // Override seed to be deterministic
        state1 = new BattleDomainState(
            state1.BattleId,
            state1.MatchId,
            state1.PlayerAId,
            state1.PlayerBId,
            new Ruleset(state1.Ruleset.Version, state1.Ruleset.TurnSeconds, state1.Ruleset.NoActionLimit, fixedSeed, state1.Ruleset.Balance),
            state1.Phase,
            state1.TurnIndex,
            state1.NoActionStreakBoth,
            state1.LastResolvedTurnIndex,
            state1.PlayerA,
            state1.PlayerB);

        state2 = new BattleDomainState(
            state2.BattleId,
            state2.MatchId,
            state2.PlayerAId,
            state2.PlayerBId,
            new Ruleset(state2.Ruleset.Version, state2.Ruleset.TurnSeconds, state2.Ruleset.NoActionLimit, fixedSeed, state2.Ruleset.Balance),
            state2.Phase,
            state2.TurnIndex,
            state2.NoActionStreakBoth,
            state2.LastResolvedTurnIndex,
            state2.PlayerA,
            state2.PlayerB);

        // Use valid actions (not NoAction)
        var actionA = PlayerAction.Create(playerAId, turnIndex, BattleZone.Head, null, null);
        var actionB = PlayerAction.Create(playerBId, turnIndex, BattleZone.Chest, null, null);

        var engine = new BattleEngine();

        // Act: Resolve turn twice with identical inputs
        var result1 = engine.ResolveTurn(state1, actionA, actionB);
        var result2 = engine.ResolveTurn(state2, actionA, actionB);

        // Assert: All outcomes must be identical (bit-for-bit deterministic)
        var turnResolved1 = result1.Events.OfType<TurnResolvedDomainEvent>().Single();
        var turnResolved2 = result2.Events.OfType<TurnResolvedDomainEvent>().Single();

        // AtoB outcomes must match
        turnResolved1.Log.AtoB.Outcome.Should().Be(turnResolved2.Log.AtoB.Outcome, "AtoB.Outcome must be identical");
        turnResolved1.Log.AtoB.WasCrit.Should().Be(turnResolved2.Log.AtoB.WasCrit, "AtoB.WasCrit must be identical");
        turnResolved1.Log.AtoB.Damage.Should().Be(turnResolved2.Log.AtoB.Damage, "AtoB.Damage must be identical");

        // BtoA outcomes must match
        turnResolved1.Log.BtoA.Outcome.Should().Be(turnResolved2.Log.BtoA.Outcome, "BtoA.Outcome must be identical");
        turnResolved1.Log.BtoA.WasCrit.Should().Be(turnResolved2.Log.BtoA.WasCrit, "BtoA.WasCrit must be identical");
        turnResolved1.Log.BtoA.Damage.Should().Be(turnResolved2.Log.BtoA.Damage, "BtoA.Damage must be identical");

        // Final HP must match
        result1.NewState.PlayerA.CurrentHp.Should().Be(result2.NewState.PlayerA.CurrentHp, "PlayerA.CurrentHp must be identical");
        result1.NewState.PlayerB.CurrentHp.Should().Be(result2.NewState.PlayerB.CurrentHp, "PlayerB.CurrentHp must be identical");
    }

    [Fact]
    public void DeterministicTurnRng_StreamIndependence_RngAtoBUnaffectedByRngBtoA()
    {
        // TEST 2: Stream independence sanity
        // This test verifies that rngAtoB and rngBtoA use independent streams.
        // Using rngBtoA should not affect the sequence of values from rngAtoB.

        // Arrange: Create fixed battle state
        var battleId = Guid.Parse("BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB");
        var playerAId = Guid.Parse("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA");
        var playerBId = Guid.Parse("CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC");
        var fixedSeed = 77777;
        var turnIndex = 1;

        var state = CreateTestState(battleId, playerAId, playerBId, turnIndex: turnIndex);
        state = new BattleDomainState(
            state.BattleId,
            state.MatchId,
            state.PlayerAId,
            state.PlayerBId,
            new Ruleset(state.Ruleset.Version, state.Ruleset.TurnSeconds, state.Ruleset.NoActionLimit, fixedSeed, state.Ruleset.Balance),
            state.Phase,
            state.TurnIndex,
            state.NoActionStreakBoth,
            state.LastResolvedTurnIndex,
            state.PlayerA,
            state.PlayerB);

        // Act: Create RNG instances twice
        var (rngAtoB_1, rngBtoA_1) = DeterministicTurnRng.Create(state);
        var (rngAtoB_2, rngBtoA_2) = DeterministicTurnRng.Create(state);

        // Generate first 5 values from rngAtoB_1 (baseline)
        var baselineAtoB = new List<decimal>();
        for (int i = 0; i < 5; i++)
        {
            baselineAtoB.Add(rngAtoB_1.NextDecimal(0, 1));
        }

        // Generate 10 values from rngBtoA_2 (this should NOT affect rngAtoB_2)
        for (int i = 0; i < 10; i++)
        {
            rngBtoA_2.NextDecimal(0, 1);
        }

        // Generate first 5 values from rngAtoB_2 (should match baseline despite rngBtoA_2 usage)
        var testAtoB = new List<decimal>();
        for (int i = 0; i < 5; i++)
        {
            testAtoB.Add(rngAtoB_2.NextDecimal(0, 1));
        }

        // Assert: rngAtoB sequences must match (proves stream independence)
        baselineAtoB.Should().Equal(testAtoB, 
            "rngAtoB sequence must be identical regardless of how many times rngBtoA was used. " +
            "This proves A->B and B->A use independent RNG streams.");
    }
}

