using Kombats.Battle.Domain.Events;
using Kombats.Battle.Domain.Model;
using Kombats.Battle.Domain.Results;
using Kombats.Battle.Domain.Rules;

namespace Kombats.Battle.Domain.Engine;

/// <summary>
/// Battle engine implementation - pure domain logic for resolving turns in fistfight combat.
/// This is a pure function with no infrastructure dependencies.
/// </summary>
public sealed class BattleEngine : IBattleEngine
{
    private readonly IRandomProvider _rng;

    public BattleEngine(IRandomProvider rng)
    {
        _rng = rng ?? throw new ArgumentNullException(nameof(rng));
    }

    public BattleResolutionResult ResolveTurn(
        BattleDomainState state,
        PlayerAction playerAAction,
        PlayerAction playerBAction)
    {
        if (state.Phase != BattlePhase.Resolving)
            throw new InvalidOperationException($"Cannot resolve turn in phase {state.Phase}");

        if (state.TurnIndex != playerAAction.TurnIndex || state.TurnIndex != playerBAction.TurnIndex)
            throw new ArgumentException($"Turn index mismatch: state={state.TurnIndex}, actions={playerAAction.TurnIndex}/{playerBAction.TurnIndex}");

        var events = new List<IDomainEvent>();
        var now = DateTime.UtcNow;

        // Create mutable copies of player states
        var playerA = new PlayerState(
            state.PlayerA.PlayerId,
            state.PlayerA.MaxHp,
            state.PlayerA.CurrentHp,
            state.PlayerA.Stats);
        var playerB = new PlayerState(
            state.PlayerB.PlayerId,
            state.PlayerB.MaxHp,
            state.PlayerB.CurrentHp,
            state.PlayerB.Stats);

        // Normalize actions: validate and convert invalid to NoAction
        var normalizedActionA = NormalizeAction(playerAAction);
        var normalizedActionB = NormalizeAction(playerBAction);

        // Check for DoubleForfeit: both players NoAction
        if (normalizedActionA.IsNoAction && normalizedActionB.IsNoAction)
        {
            var newStreak = state.NoActionStreakBoth + 1;

            if (newStreak >= state.Ruleset.NoActionLimit)
            {
                // Battle ends due to DoubleForfeit
                var endedState = new BattleDomainState(
                    state.BattleId,
                    state.MatchId,
                    state.PlayerAId,
                    state.PlayerBId,
                    state.Ruleset,
                    BattlePhase.Ended,
                    state.TurnIndex,
                    newStreak,
                    state.TurnIndex,
                    playerA,
                    playerB);

                endedState.EndBattle();

                events.Add(new BattleEndedDomainEvent(
                    state.BattleId,
                    WinnerPlayerId: null,
                    EndBattleReason.DoubleForfeit,
                    state.TurnIndex,
                    now));

                return new BattleResolutionResult
                {
                    NewState = endedState,
                    Events = events
                };
            }

            // Streak increased but battle continues
            var continuingState = new BattleDomainState(
                state.BattleId,
                state.MatchId,
                state.PlayerAId,
                state.PlayerBId,
                state.Ruleset,
                BattlePhase.Resolving,
                state.TurnIndex,
                newStreak,
                state.LastResolvedTurnIndex,
                playerA,
                playerB);

            continuingState.UpdateNoActionStreak(newStreak);

            // Create turn resolution log for DoubleForfeit (both NoAction)
            var doubleForfeitLog = new TurnResolutionLog
            {
                BattleId = state.BattleId,
                TurnIndex = state.TurnIndex,
                AtoB = new AttackResolution
                {
                    AttackerId = state.PlayerAId,
                    DefenderId = state.PlayerBId,
                    TurnIndex = state.TurnIndex,
                    AttackZone = null,
                    DefenderBlockPrimary = null,
                    DefenderBlockSecondary = null,
                    WasBlocked = false,
                    WasCrit = false,
                    Outcome = AttackOutcome.NoAction,
                    Damage = 0
                },
                BtoA = new AttackResolution
                {
                    AttackerId = state.PlayerBId,
                    DefenderId = state.PlayerAId,
                    TurnIndex = state.TurnIndex,
                    AttackZone = null,
                    DefenderBlockPrimary = null,
                    DefenderBlockSecondary = null,
                    WasBlocked = false,
                    WasCrit = false,
                    Outcome = AttackOutcome.NoAction,
                    Damage = 0
                }
            };

            events.Add(new TurnResolvedDomainEvent(
                state.BattleId,
                state.TurnIndex,
                normalizedActionA,
                normalizedActionB,
                doubleForfeitLog,
                now));

            return new BattleResolutionResult
            {
                NewState = continuingState,
                Events = events
            };
        }

        // Reset streak if at least one player acted
        var resetStreak = 0;

        // Resolve attacks simultaneously (order must not matter)
        // Attack from A to B
        var resolutionAtoB = ResolveAttack(
            normalizedActionA,
            normalizedActionB,
            playerA.Stats,
            playerB.Stats,
            state.PlayerAId,
            state.PlayerBId,
            state.TurnIndex,
            state.Ruleset);

        // Attack from B to A
        var resolutionBtoA = ResolveAttack(
            normalizedActionB,
            normalizedActionA,
            playerB.Stats,
            playerA.Stats,
            state.PlayerBId,
            state.PlayerAId,
            state.TurnIndex,
            state.Ruleset);

        // Apply damage to starting HP of the turn (simultaneous)
        if (resolutionAtoB.Damage > 0 && playerB.IsAlive)
        {
            playerB.ApplyDamage(resolutionAtoB.Damage);
            events.Add(new PlayerDamagedDomainEvent(
                state.BattleId,
                state.PlayerBId,
                resolutionAtoB.Damage,
                playerB.CurrentHp,
                state.TurnIndex,
                now));
        }

        if (resolutionBtoA.Damage > 0 && playerA.IsAlive)
        {
            playerA.ApplyDamage(resolutionBtoA.Damage);
            events.Add(new PlayerDamagedDomainEvent(
                state.BattleId,
                state.PlayerAId,
                resolutionBtoA.Damage,
                playerA.CurrentHp,
                state.TurnIndex,
                now));
        }

        // Create turn resolution log
        var turnLog = new TurnResolutionLog
        {
            BattleId = state.BattleId,
            TurnIndex = state.TurnIndex,
            AtoB = resolutionAtoB,
            BtoA = resolutionBtoA
        };

        // Check for battle end due to player death
        var winnerId = (Guid?)null;
        var endReason = EndBattleReason.Normal;

        if (playerA.IsDead && playerB.IsDead)
        {
            // Both dead - draw
            endReason = EndBattleReason.Normal; // Could be a separate "Draw" reason
            winnerId = null;
        }
        else if (playerA.IsDead)
        {
            winnerId = state.PlayerBId;
            endReason = EndBattleReason.Normal;
        }
        else if (playerB.IsDead)
        {
            winnerId = state.PlayerAId;
            endReason = EndBattleReason.Normal;
        }

        // Create new state
        BattleDomainState newState;
        if (winnerId.HasValue || (playerA.IsDead && playerB.IsDead))
        {
            // Battle ended
            newState = new BattleDomainState(
                state.BattleId,
                state.MatchId,
                state.PlayerAId,
                state.PlayerBId,
                state.Ruleset,
                BattlePhase.Ended,
                state.TurnIndex,
                resetStreak,
                state.TurnIndex,
                playerA,
                playerB);

            newState.EndBattle();

            events.Add(new BattleEndedDomainEvent(
                state.BattleId,
                winnerId,
                endReason,
                state.TurnIndex,
                now));
        }
        else
        {
            // Battle continues
            newState = new BattleDomainState(
                state.BattleId,
                state.MatchId,
                state.PlayerAId,
                state.PlayerBId,
                state.Ruleset,
                BattlePhase.Resolving,
                state.TurnIndex,
                resetStreak,
                state.LastResolvedTurnIndex,
                playerA,
                playerB);

            newState.UpdateNoActionStreak(resetStreak);

            events.Add(new TurnResolvedDomainEvent(
                state.BattleId,
                state.TurnIndex,
                normalizedActionA,
                normalizedActionB,
                turnLog,
                now));
        }

        return new BattleResolutionResult
        {
            NewState = newState,
            Events = events
        };
    }

    /// <summary>
    /// Normalizes an action: validates block pattern and converts invalid to NoAction.
    /// </summary>
    private static PlayerAction NormalizeAction(PlayerAction action)
    {
        if (action.IsNoAction)
            return action;

        // If attack zone is null, it's NoAction
        if (action.AttackZone == null)
        {
            return PlayerAction.NoAction(action.PlayerId, action.TurnIndex);
        }

        // If block zones are provided, validate adjacency
        if (action.BlockZonePrimary != null && action.BlockZoneSecondary != null)
        {
            if (!BattleZoneHelper.IsValidBlockPattern(action.BlockZonePrimary.Value, action.BlockZoneSecondary.Value))
            {
                // Invalid block pattern -> NoAction
                return PlayerAction.NoAction(action.PlayerId, action.TurnIndex);
            }
        }

        // Action is valid
        return action;
    }

    /// <summary>
    /// Resolves an attack from attacker to defender, returning detailed resolution.
    /// 
    /// ORDER (Block → Dodge → Crit → Damage):
    /// 1. if NoAction -> Outcome = NoAction, damage 0
    /// 2. check block via BattleZoneHelper
    /// 3. compute derivedAtt / derivedDef via CombatMath
    /// 4. roll crit (same logic as before, no changes to formulas)
    /// 5. Block resolution:
    ///    - If isBlocked:
    ///      - If isCrit AND CritEffect.Mode == BypassBlock: attack passes block (continue to damage)
    ///      - If isCrit AND CritEffect.Mode == Hybrid: attack passes but damage reduced (continue)
    ///      - Else: return Outcome = Blocked, Damage = 0. STOP. (Do NOT roll dodge)
    /// 6. Dodge roll (ONLY if not returned as Blocked):
    ///    - if rng < ComputeDodgeChance -> Outcome = Dodged, damage 0. STOP.
    /// 7. roll base damage and apply crit multiplier exactly as current code
    /// 8. Round AwayFromZero and return Outcome + Damage
    /// </summary>
    private AttackResolution ResolveAttack(
        PlayerAction attackerAction,
        PlayerAction defenderAction,
        PlayerStats attackerStats,
        PlayerStats defenderStats,
        Guid attackerId,
        Guid defenderId,
        int turnIndex,
        Ruleset ruleset)
    {
        // 1. If attacker is NoAction, return NoAction outcome
        if (attackerAction.IsNoAction || attackerAction.AttackZone == null)
        {
            return BuildResolution(
                attackerId,
                defenderId,
                turnIndex,
                attackZone: null,
                defenderAction.BlockZonePrimary,
                defenderAction.BlockZoneSecondary,
                wasBlocked: false,
                wasCrit: false,
                AttackOutcome.NoAction,
                damage: 0);
        }

        // 2. Check if attack is blocked
        var attackZone = attackerAction.AttackZone.Value;
        var isBlocked = ComputeBlockStatus(
            attackZone,
            defenderAction.BlockZonePrimary,
            defenderAction.BlockZoneSecondary);

        // 3. Compute derived stats
        var derivedAtt = ComputeDerivedStats(attackerStats, ruleset.Balance);
        var derivedDef = ComputeDerivedStats(defenderStats, ruleset.Balance);

        // 4. Roll crit (same logic as before, no changes to formulas)
        var isCrit = RollCrit(derivedAtt, derivedDef, ruleset.Balance);

        // 5. Block resolution
        var blockResult = ResolveBlock(isBlocked, isCrit, ruleset.Balance.CritEffect);
        if (blockResult.HasValue)
        {
            // Blocked and not bypassed - return immediately (do NOT roll dodge)
            return BuildResolution(
                attackerId,
                defenderId,
                turnIndex,
                attackZone,
                defenderAction.BlockZonePrimary,
                defenderAction.BlockZoneSecondary,
                wasBlocked: true,
                wasCrit: false,
                AttackOutcome.Blocked,
                damage: 0);
        }

        // 6. Dodge roll (ONLY if not returned as Blocked)
        var dodgeResult = RollDodge(derivedAtt, derivedDef, ruleset.Balance);
        if (dodgeResult.HasValue)
        {
            return BuildResolution(
                attackerId,
                defenderId,
                turnIndex,
                attackZone,
                defenderAction.BlockZonePrimary,
                defenderAction.BlockZoneSecondary,
                wasBlocked: isBlocked,
                wasCrit: false,
                AttackOutcome.Dodged,
                damage: 0);
        }

        // 7. Roll base damage and apply crit multiplier
        var damage = ComputeFinalDamage(
            derivedAtt,
            isCrit,
            isBlocked,
            ruleset.Balance.CritEffect);

        // 8. Determine final outcome
        var outcome = DetermineOutcome(isCrit, isBlocked, ruleset.Balance.CritEffect.Mode);

        // Enforce invariants
        EnforceInvariants(damage, outcome);

        return BuildResolution(
            attackerId,
            defenderId,
            turnIndex,
            attackZone,
            defenderAction.BlockZonePrimary,
            defenderAction.BlockZoneSecondary,
            wasBlocked: isBlocked,
            wasCrit: isCrit,
            outcome,
            damage);
    }

    /// <summary>
    /// Factory method to build AttackResolution consistently.
    /// </summary>
    private static AttackResolution BuildResolution(
        Guid attackerId,
        Guid defenderId,
        int turnIndex,
        BattleZone? attackZone,
        BattleZone? defenderBlockPrimary,
        BattleZone? defenderBlockSecondary,
        bool wasBlocked,
        bool wasCrit,
        AttackOutcome outcome,
        int damage)
    {
        return new AttackResolution
        {
            AttackerId = attackerId,
            DefenderId = defenderId,
            TurnIndex = turnIndex,
            AttackZone = attackZone,
            DefenderBlockPrimary = defenderBlockPrimary,
            DefenderBlockSecondary = defenderBlockSecondary,
            WasBlocked = wasBlocked,
            WasCrit = wasCrit,
            Outcome = outcome,
            Damage = damage
        };
    }

    /// <summary>
    /// Computes whether an attack is blocked.
    /// </summary>
    private static bool ComputeBlockStatus(
        BattleZone attackZone,
        BattleZone? defenderBlockPrimary,
        BattleZone? defenderBlockSecondary)
    {
        return BattleZoneHelper.IsZoneBlocked(
            attackZone,
            defenderBlockPrimary,
            defenderBlockSecondary);
    }

    /// <summary>
    /// Computes derived combat stats.
    /// </summary>
    private static DerivedCombatStats ComputeDerivedStats(PlayerStats stats, CombatBalance balance)
    {
        return CombatMath.ComputeDerived(stats, balance);
    }

    /// <summary>
    /// Rolls crit chance and returns whether crit occurred.
    /// </summary>
    private bool RollCrit(
        DerivedCombatStats attackerDerived,
        DerivedCombatStats defenderDerived,
        CombatBalance balance)
    {
        var critChance = CombatMath.ComputeCritChance(attackerDerived, defenderDerived, balance);
        var critRoll = _rng.NextDecimal(0, 1);
        return critRoll < critChance;
    }

    /// <summary>
    /// Resolves block status considering crit penetration.
    /// Returns true if attack is fully blocked (no damage), null if crit bypasses/hybrid.
    /// </summary>
    private static bool? ResolveBlock(bool isBlocked, bool isCrit, CritEffectBalance critEffect)
    {
        if (!isBlocked)
            return null;

        // Check if crit bypasses or hybrid penetrates block
        if (isCrit && critEffect.Mode == CritEffectMode.BypassBlock)
            return null; // Crit bypasses block - continue to damage
        if (isCrit && critEffect.Mode == CritEffectMode.Hybrid)
            return null; // Hybrid penetrates block - continue to damage

        // Fully blocked
        return true;
    }

    /// <summary>
    /// Rolls dodge chance and returns whether dodge occurred.
    /// Returns true if dodged, null if not.
    /// </summary>
    private bool? RollDodge(
        DerivedCombatStats attackerDerived,
        DerivedCombatStats defenderDerived,
        CombatBalance balance)
    {
        var dodgeChance = CombatMath.ComputeDodgeChance(attackerDerived, defenderDerived, balance);
        var dodgeRoll = _rng.NextDecimal(0, 1);
        return dodgeRoll < dodgeChance ? true : null;
    }

    /// <summary>
    /// Computes final damage with crit multipliers applied.
    /// </summary>
    private int ComputeFinalDamage(
        DerivedCombatStats attackerDerived,
        bool isCrit,
        bool isBlocked,
        CritEffectBalance critEffect)
    {
        // Roll base damage
        decimal baseDamage = CombatMath.RollDamage(_rng, attackerDerived);

        // Apply crit multiplier if crit occurred
        decimal finalDamage = baseDamage;
        if (isCrit)
        {
            if (isBlocked && critEffect.Mode == CritEffectMode.Hybrid)
            {
                // Hybrid mode: apply both multiplier and block reduction
                finalDamage = baseDamage * critEffect.Multiplier * critEffect.HybridBlockMultiplier;
            }
            else
            {
                // Normal crit: apply multiplier
                finalDamage = baseDamage * critEffect.Multiplier;
            }
        }

        // Round AwayFromZero
        return (int)Math.Round(finalDamage, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Determines the final outcome based on crit and block status.
    /// </summary>
    private static AttackOutcome DetermineOutcome(bool isCrit, bool isBlocked, CritEffectMode critMode)
    {
        if (isCrit && isBlocked && critMode == CritEffectMode.Hybrid)
            return AttackOutcome.CriticalHybridBlocked;
        if (isCrit && isBlocked && critMode == CritEffectMode.BypassBlock)
            return AttackOutcome.CriticalBypassBlock;
        if (isCrit)
            return AttackOutcome.CriticalHit;
        return AttackOutcome.Hit;
    }

    /// <summary>
    /// Enforces invariants on damage and outcome.
    /// </summary>
    private static void EnforceInvariants(int damage, AttackOutcome outcome)
    {
        // Invariant 1: If Damage == 0, Outcome MUST be one of: NoAction, Dodged, Blocked
        if (damage == 0 && outcome != AttackOutcome.NoAction && outcome != AttackOutcome.Dodged && outcome != AttackOutcome.Blocked)
        {
            throw new InvalidOperationException(
                $"Invariant violation: Damage is 0 but Outcome is {outcome}. " +
                $"Damage=0 requires Outcome to be NoAction, Dodged, or Blocked.");
        }

        // Invariant 2: If Outcome is Critical*, then Damage MUST be > 0
        if ((outcome == AttackOutcome.CriticalHit || 
             outcome == AttackOutcome.CriticalBypassBlock || 
             outcome == AttackOutcome.CriticalHybridBlocked) && 
            damage <= 0)
        {
            throw new InvalidOperationException(
                $"Invariant violation: Outcome is {outcome} but Damage is {damage}. " +
                $"Critical outcomes require Damage > 0.");
        }
    }
}



