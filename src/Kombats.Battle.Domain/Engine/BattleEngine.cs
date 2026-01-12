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

            events.Add(new TurnResolvedDomainEvent(
                state.BattleId,
                state.TurnIndex,
                normalizedActionA,
                normalizedActionB,
                now));

            return new BattleResolutionResult
            {
                NewState = continuingState,
                Events = events
            };
        }

        // Reset streak if at least one player acted
        var resetStreak = 0;

        // Calculate damage simultaneously (order must not matter)
        // Damage from A to B
        int damageToB = CalculateDamage(
            normalizedActionA,
            normalizedActionB,
            playerA.Stats,
            playerB.Stats,
            state.Ruleset);

        // Damage from B to A
        int damageToA = CalculateDamage(
            normalizedActionB,
            normalizedActionA,
            playerB.Stats,
            playerA.Stats,
            state.Ruleset);

        // Apply damage to starting HP of the turn (simultaneous)
        if (damageToB > 0 && playerB.IsAlive)
        {
            playerB.ApplyDamage(damageToB);
            events.Add(new PlayerDamagedDomainEvent(
                state.BattleId,
                state.PlayerBId,
                damageToB,
                playerB.CurrentHp,
                state.TurnIndex,
                now));
        }

        if (damageToA > 0 && playerA.IsAlive)
        {
            playerA.ApplyDamage(damageToA);
            events.Add(new PlayerDamagedDomainEvent(
                state.BattleId,
                state.PlayerAId,
                damageToA,
                playerA.CurrentHp,
                state.TurnIndex,
                now));
        }

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
    /// Calculates damage from attacker to defender.
    /// Returns 0 if attacker is NoAction, attack is dodged, or attack is blocked (unless crit bypasses block).
    /// 
    /// ORDER MUST BE EXACTLY:
    /// 1. if NoAction -> return 0
    /// 2. check block via BattleZoneHelper
    /// 3. compute derivedAtt / derivedDef via CombatMath
    /// 4. roll dodge: if rng < ComputeDodgeChance -> return 0
    /// 5. roll crit (BEFORE resolving block result): isCrit = rng < ComputeCritChance
    /// 6. if blocked:
    ///    - if isCrit AND CritEffect.Mode == BypassBlock -> damage passes
    ///    - if isCrit AND CritEffect.Mode == Hybrid -> damage reduced by HybridBlockMultiplier
    ///    - else -> return 0
    /// 7. roll base damage
    /// 8. if isCrit -> apply CritEffect.Multiplier
    /// 9. return rounded damage (AwayFromZero)
    /// </summary>
    private int CalculateDamage(
        PlayerAction attackerAction,
        PlayerAction defenderAction,
        PlayerStats attackerStats,
        PlayerStats defenderStats,
        Ruleset ruleset)
    {
        // 1. If attacker is NoAction, no damage
        if (attackerAction.IsNoAction || attackerAction.AttackZone == null)
        {
            return 0;
        }

        // 2. Check if attack is blocked
        var attackZone = attackerAction.AttackZone.Value;
        var isBlocked = BattleZoneHelper.IsZoneBlocked(
            attackZone,
            defenderAction.BlockZonePrimary,
            defenderAction.BlockZoneSecondary);

        // 3. Compute derived stats
        var derivedAtt = CombatMath.ComputeDerived(attackerStats, ruleset.Balance);
        var derivedDef = CombatMath.ComputeDerived(defenderStats, ruleset.Balance);

        // 4. Roll dodge
        var dodgeChance = CombatMath.ComputeDodgeChance(derivedAtt, derivedDef, ruleset.Balance);
        var dodgeRoll = _rng.NextDecimal(0, 1);
        if (dodgeRoll < dodgeChance)
        {
            return 0;
        }

        // 5. Roll crit (BEFORE resolving block result)
        var critChance = CombatMath.ComputeCritChance(derivedAtt, derivedDef, ruleset.Balance);
        var critRoll = _rng.NextDecimal(0, 1);
        var isCrit = critRoll < critChance;

        // 6. Handle block (with crit penetration)
        if (isBlocked)
        {
            if (isCrit && ruleset.Balance.CritEffect.Mode == CritEffectMode.BypassBlock)
            {
                // Crit bypasses block - damage passes
            }
            else if (isCrit && ruleset.Balance.CritEffect.Mode == CritEffectMode.Hybrid)
            {
                // Hybrid mode: damage reduced by HybridBlockMultiplier
                // Continue to damage calculation with multiplier applied later
            }
            else
            {
                // Blocked - no damage
                return 0;
            }
        }

        // 7. Roll base damage
        decimal baseDamage = CombatMath.RollDamage(_rng, derivedAtt);

        // 8. Apply crit multiplier if crit occurred
        decimal finalDamage = baseDamage;
        if (isCrit)
        {
            if (isBlocked && ruleset.Balance.CritEffect.Mode == CritEffectMode.Hybrid)
            {
                // Hybrid mode: apply both multiplier and block reduction
                finalDamage = baseDamage * ruleset.Balance.CritEffect.Multiplier * ruleset.Balance.CritEffect.HybridBlockMultiplier;
            }
            else
            {
                // Normal crit: apply multiplier
                finalDamage = baseDamage * ruleset.Balance.CritEffect.Multiplier;
            }
        }

        // 9. Return rounded damage (AwayFromZero)
        return (int)Math.Round(finalDamage, MidpointRounding.AwayFromZero);
    }
}


