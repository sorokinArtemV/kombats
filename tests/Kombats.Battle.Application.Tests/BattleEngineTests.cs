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
        var rng = new DeterministicRandomProvider(0.5m);
        var engine = new BattleEngine(rng);
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
        // New order: crit roll → block check → dodge roll (if not blocked) → damage roll
        // Use rng value < 0.05 to trigger dodge
        var rng = new SequentialValueProvider([0.1m, 0.01m, 0.5m, 0.1m, 0.1m, 0.5m]); // AtoB: crit (no), dodge (<0.05), damage; BtoA: crit (no), dodge (no), damage
        var engine = new BattleEngine(rng);
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
        // New order: crit roll → block check → dodge roll (if not blocked) → damage roll
        // When blocked and no crit bypass, dodge should NOT be rolled
        // AtoB: crit roll, blocked → Blocked (dodge not rolled)
        var rng = new SequentialValueProvider([0.1m, 0.1m, 0.5m, 0.1m, 0.1m, 0.5m]); // AtoB: crit (no), blocked; BtoA: crit (no), dodge (no), damage
        var engine = new BattleEngine(rng);
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
        // New order: crit roll → block check → dodge roll (if not blocked) → damage roll
        // AtoB: crit (yes, <0.03), bypasses block, dodge (no), damage; BtoA: crit (no), blocked, no dodge roll
        var rng = new SequentialValueProvider([0.01m, 0.1m, 0.5m, 0.1m, 0.5m]); // AtoB: crit (yes), dodge (no), damage; BtoA: crit (no), blocked
        var engine = new BattleEngine(rng);
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
        // New order: crit roll → block check → dodge roll (if not blocked) → damage roll
        // AtoB: crit (yes, <0.03), hybrid penetrates block, dodge (no), damage; BtoA: crit (no), blocked, no dodge roll
        var rng = new SequentialValueProvider([0.01m, 0.1m, 0.5m, 0.1m, 0.5m]); // AtoB: crit (yes), dodge (no), damage; BtoA: crit (no), blocked
        var engine = new BattleEngine(rng);
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
        // New order: crit roll → block check → dodge roll (if not blocked) → damage roll
        var rng = new SequentialValueProvider([0.01m, 0.1m, 0.5m, 0.1m, 0.1m, 0.5m]); // AtoB: crit (yes), dodge (no), damage; BtoA: crit (no), dodge (no), damage
        var engine = new BattleEngine(rng);
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
        // New order: crit roll → block check → dodge roll (if not blocked) → damage roll
        var rng = new SequentialValueProvider([0.1m, 0.1m, 0.5m, 0.1m, 0.1m, 0.5m]); // AtoB: crit (no), dodge (no), damage; BtoA: crit (no), dodge (no), damage
        var engine = new BattleEngine(rng);
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
        var rng = new SequentialValueProvider([0.1m, 0.1m, 0.5m, 0.1m, 0.1m, 0.5m]);
        var engine = new BattleEngine(rng);
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
        var rng = new DeterministicRandomProvider(0.5m);
        var engine = new BattleEngine(rng);
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
        // New order: crit roll → block check → dodge roll (if not blocked) → damage roll
        var rng = new SequentialValueProvider([0.1m, 0.1m, 0.5m, 0.1m, 0.1m, 0.5m]); // AtoB: crit (no), blocked; BtoA: crit (no), dodge (no), damage
        var engine = new BattleEngine(rng);
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
        // New order: crit roll → block check → dodge roll (if not blocked) → damage roll
        var rng = new SequentialValueProvider([0.01m, 0.1m, 0.5m, 0.1m, 0.1m, 0.5m]); // AtoB: crit (yes), dodge (no), damage; BtoA: crit (no), dodge (no), damage
        var engine = new BattleEngine(rng);
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
        // This test verifies that when an attack is blocked (and crit does NOT bypass/hybrid),
        // dodge is NOT rolled. The test sets up a scenario where:
        // - Attack is blocked
        // - If dodge were rolled with the given RNG, it would succeed (dodge chance ~0.05, RNG < 0.05)
        // - But since it's blocked, dodge should not be evaluated and outcome should be Blocked
        // New order: crit roll → block check → dodge roll (if not blocked) → damage roll
        // AtoB: crit roll (0.1m - no crit), blocked → should return Blocked without rolling dodge
        // Note: We can't easily verify RNG consumption without a mock that tracks calls,
        // but we can verify the correct outcome (Blocked) when attack is blocked
        var rng = new SequentialValueProvider([0.1m, 0.01m, 0.5m, 0.1m, 0.1m, 0.5m]); 
        // AtoB: crit (no), blocked (dodge would succeed if rolled, but shouldn't be rolled); BtoA: crit (no), dodge (no), damage
        var engine = new BattleEngine(rng);
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
        
        // AtoB should be Blocked (dodge was not rolled, even though RNG would have succeeded)
        // This proves block has precedence over dodge
        turnResolvedEvent.Log.AtoB.Outcome.Should().Be(AttackOutcome.Blocked);
        turnResolvedEvent.Log.AtoB.Damage.Should().Be(0);
        turnResolvedEvent.Log.AtoB.WasBlocked.Should().BeTrue();
    }
}

