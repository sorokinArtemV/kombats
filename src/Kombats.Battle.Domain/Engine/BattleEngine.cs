using Kombats.Battle.Domain.Events;
using Kombats.Battle.Domain.Model;
using Kombats.Battle.Domain.Results;
using Kombats.Battle.Domain.Rules;

namespace Kombats.Battle.Domain.Engine;

/// <summary>
/// Battle engine implementation - pure domain logic for resolving turns in fistfight combat.
/// Battle outcome is deterministic: same seed + state + actions => same results (damage, crit, dodge, outcomes).
/// </summary>
public sealed class BattleEngine : IBattleEngine
{
    public BattleResolutionResult ResolveTurn(
        BattleDomainState state,
        PlayerAction playerAAction,
        PlayerAction playerBAction)
    {
        if (state.Phase != BattlePhase.Resolving)
        {
            throw new InvalidOperationException($"Cannot resolve turn in phase {state.Phase}");
        }

        if (state.TurnIndex != playerAAction.TurnIndex || state.TurnIndex != playerBAction.TurnIndex)
        {
            throw new ArgumentException($"Turn index mismatch: state={state.TurnIndex}, actions={playerAAction.TurnIndex}/{playerBAction.TurnIndex}");}

        var events = new List<IDomainEvent>();
        var now = DateTime.UtcNow;
        
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
        var (normalizedActionA, normalizedActionB) = NormalizeActions(playerAAction, playerBAction);

        // Check for DoubleForfeit: both players NoAction
        var doubleForfeitResult = TryResolveDoubleForfeit(
            state, normalizedActionA, 
            normalizedActionB, 
            playerA, 
            playerB, 
            now);
        
        if (doubleForfeitResult != null)
        {
            return doubleForfeitResult;
        }

        // Reset streak if at least one player acted
        var resetStreak = 0;

        // Create deterministic RNG instances for this turn
        // This ensures order independence: A->B and B->A use separate RNG streams
        var (rngAtoB, rngBtoA) = DeterministicTurnRng.Create(state);

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
            state.Ruleset,
            rngAtoB);

        // Attack from B to A
        var resolutionBtoA = ResolveAttack(
            normalizedActionB,
            normalizedActionA,
            playerB.Stats,
            playerA.Stats,
            state.PlayerBId,
            state.PlayerAId,
            state.TurnIndex,
            state.Ruleset,
            rngBtoA);

        // Apply damage and create events
        ApplySimultaneousDamageAndEvents(state, resolutionAtoB, resolutionBtoA, playerA, playerB, now, events);

        // Create turn resolution log
        var turnLog = new TurnResolutionLog
        {
            BattleId = state.BattleId,
            TurnIndex = state.TurnIndex,
            AtoB = resolutionAtoB,
            BtoA = resolutionBtoA
        };

        // Check for battle end due to player death
        var (winnerId, endReason) = DetermineBattleEnd(playerA, playerB, state);

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
    /// Normalizes both player actions: validates and converts invalid to NoAction.
    /// </summary>
    private static (PlayerAction normalizedA, PlayerAction normalizedB) NormalizeActions(
        PlayerAction actionA,
        PlayerAction actionB)
    {
        return (NormalizeAction(actionA), NormalizeAction(actionB));
    }

    /// <summary>
    /// Attempts to resolve double forfeit (both players NoAction).
    /// Returns resolution result if double forfeit occurred, null otherwise.
    /// </summary>
    private static BattleResolutionResult? TryResolveDoubleForfeit(
        BattleDomainState state,
        PlayerAction normalizedActionA,
        PlayerAction normalizedActionB,
        PlayerState playerA,
        PlayerState playerB,
        DateTime now)
    {
        if (!normalizedActionA.IsNoAction || !normalizedActionB.IsNoAction)
            return null;

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

            return new BattleResolutionResult
            {
                NewState = endedState,
                Events = new List<IDomainEvent>
                {
                    new BattleEndedDomainEvent(
                        state.BattleId,
                        WinnerPlayerId: null,
                        EndBattleReason.DoubleForfeit,
                        state.TurnIndex,
                        now)
                }
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

        return new BattleResolutionResult
        {
            NewState = continuingState,
            Events = new List<IDomainEvent>
            {
                new TurnResolvedDomainEvent(
                    state.BattleId,
                    state.TurnIndex,
                    normalizedActionA,
                    normalizedActionB,
                    doubleForfeitLog,
                    now)
            }
        };
    }

    /// <summary>
    /// Applies simultaneous damage to both players and creates damage events.
    /// Adds events to the provided list (does not create a new list).
    /// </summary>
    private static void ApplySimultaneousDamageAndEvents(
        BattleDomainState state,
        AttackResolution resolutionAtoB,
        AttackResolution resolutionBtoA,
        PlayerState playerA,
        PlayerState playerB,
        DateTime now,
        List<IDomainEvent> events)
    {
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
    }

    /// <summary>
    /// Determines if battle should end and returns winner ID and end reason.
    /// </summary>
    private static (Guid? winnerId, EndBattleReason endReason) DetermineBattleEnd(
        PlayerState playerA,
        PlayerState playerB,
        BattleDomainState state)
    {
        if (playerA.IsDead && playerB.IsDead)
        {
            // Both dead - draw
            return (null, EndBattleReason.Normal);
        }
        else if (playerA.IsDead)
        {
            return (state.PlayerBId, EndBattleReason.Normal);
        }
        else if (playerB.IsDead)
        {
            return (state.PlayerAId, EndBattleReason.Normal);
        }

        return (null, EndBattleReason.Normal);
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
    /// AUTHORITATIVE RESOLUTION ORDER:
    /// 1) NoAction -> Outcome = NoAction, Damage = 0
    /// 2) Compute derived stats via CombatMath.ComputeDerived
    /// 3) Roll Dodge FIRST (hit/miss):
    ///    - If dodged => Outcome = Dodged, Damage = 0. STOP.
    /// 4) If hit, then evaluate Block mitigation:
    ///    - zoneMatched = BattleZoneHelper.IsZoneBlocked(attackZone, defenderBlockPrimary, defenderBlockSecondary)
    ///    - Roll Crit (same logic as current; chance formula unchanged)
    ///    - If zoneMatched:
    ///        - If isCrit and mode==BypassBlock => treat as "block ignored", proceed to damage.
    ///          Outcome later must become CriticalBypassBlock.
    ///        - If isCrit and mode==Hybrid => proceed to damage but apply HybridBlockMultiplier.
    ///          Outcome later must become CriticalHybridBlocked.
    ///        - Else => Outcome = Blocked, Damage = 0. STOP. (This is mitigation, not miss.)
    /// 5) If not blocked (zoneMatched==false) OR block was bypassed/hybridized:
    ///    - Roll base damage via CombatMath.RollDamage
    ///    - If isCrit => apply CritEffect.Multiplier
    ///    - If hybrid => also apply CritEffect.HybridBlockMultiplier
    ///    - Round AwayFromZero
    ///    - Outcome:
    ///        - isCrit && zoneMatched && mode==BypassBlock => CriticalBypassBlock
    ///        - isCrit && zoneMatched && mode==Hybrid => CriticalHybridBlocked
    ///        - isCrit => CriticalHit
    ///        - else => Hit
    /// </summary>
    private AttackResolution ResolveAttack(
        PlayerAction attackerAction,
        PlayerAction defenderAction,
        PlayerStats attackerStats,
        PlayerStats defenderStats,
        Guid attackerId,
        Guid defenderId,
        int turnIndex,
        Ruleset ruleset,
        IRandomProvider rng)
    {
        // 1. If NoAction -> Outcome = NoAction, Damage = 0
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

        var attackZone = attackerAction.AttackZone.Value;

        // 2. Compute derived stats via CombatMath.ComputeDerived
        var derivedAtt = ComputeDerivedStats(attackerStats, ruleset.Balance);
        var derivedDef = ComputeDerivedStats(defenderStats, ruleset.Balance);

        // Check zoneMatched early for WasBlocked flag (represents whether defender had zone covered)
        // This is separate from block mitigation logic which happens after dodge
        var zoneMatched = BattleZoneHelper.IsZoneBlocked(
            attackZone,
            defenderAction.BlockZonePrimary,
            defenderAction.BlockZoneSecondary);

        // 3. Roll Dodge FIRST (hit/miss)
        var isDodged = RollDodge(derivedAtt, derivedDef, ruleset.Balance, rng);
        if (isDodged)
        {
            return BuildResolution(
                attackerId,
                defenderId,
                turnIndex,
                attackZone,
                defenderAction.BlockZonePrimary,
                defenderAction.BlockZoneSecondary,
                wasBlocked: zoneMatched, // WasBlocked represents zoneMatched, not outcome
                wasCrit: false,
                AttackOutcome.Dodged,
                damage: 0);
        }

        // 4. If hit, then evaluate Block mitigation

        // Roll Crit
        var isCrit = RollCrit(derivedAtt, derivedDef, ruleset.Balance, rng);

        // Determine if block is bypassed or hybridized
        var isBlockBypassed = zoneMatched && isCrit && ruleset.Balance.CritEffect.Mode == CritEffectMode.BypassBlock;
        var isBlockHybridized = zoneMatched && isCrit && ruleset.Balance.CritEffect.Mode == CritEffectMode.Hybrid;
        var isFullyBlocked = zoneMatched && !isBlockBypassed && !isBlockHybridized;

        // If fully blocked, return Blocked (mitigation, not miss)
        if (isFullyBlocked)
        {
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

        // 5. If not blocked (zoneMatched==false) OR block was bypassed/hybridized:
        // Roll base damage
        decimal baseDamage = CombatMath.RollDamage(rng, derivedAtt);

        // Apply crit multiplier if crit occurred
        decimal finalDamage = baseDamage;
        if (isCrit)
        {
            finalDamage = baseDamage * ruleset.Balance.CritEffect.Multiplier;
        }

        // If hybrid, also apply HybridBlockMultiplier
        if (isBlockHybridized)
        {
            finalDamage = finalDamage * ruleset.Balance.CritEffect.HybridBlockMultiplier;
        }

        // Round AwayFromZero
        var damage = (int)Math.Round(finalDamage, MidpointRounding.AwayFromZero);

        // Handle edge case: if damage becomes 0, treat as Blocked
        if (damage == 0)
        {
            return BuildResolution(
                attackerId,
                defenderId,
                turnIndex,
                attackZone,
                defenderAction.BlockZonePrimary,
                defenderAction.BlockZoneSecondary,
                wasBlocked: zoneMatched,
                wasCrit: false,
                AttackOutcome.Blocked,
                damage: 0);
        }

        // Determine outcome
        AttackOutcome outcome;
        if (isCrit && zoneMatched && ruleset.Balance.CritEffect.Mode == CritEffectMode.BypassBlock)
        {
            outcome = AttackOutcome.CriticalBypassBlock;
        }
        else if (isCrit && zoneMatched && ruleset.Balance.CritEffect.Mode == CritEffectMode.Hybrid)
        {
            outcome = AttackOutcome.CriticalHybridBlocked;
        }
        else if (isCrit)
        {
            outcome = AttackOutcome.CriticalHit;
        }
        else
        {
            outcome = AttackOutcome.Hit;
        }

        return BuildResolution(
            attackerId,
            defenderId,
            turnIndex,
            attackZone,
            defenderAction.BlockZonePrimary,
            defenderAction.BlockZoneSecondary,
            wasBlocked: zoneMatched,
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
    /// Computes derived combat stats.
    /// </summary>
    private static DerivedCombatStats ComputeDerivedStats(PlayerStats stats, CombatBalance balance)
    {
        return CombatMath.ComputeDerived(stats, balance);
    }

    /// <summary>
    /// Rolls crit chance and returns whether crit occurred.
    /// </summary>
    private static bool RollCrit(
        DerivedCombatStats attackerDerived,
        DerivedCombatStats defenderDerived,
        CombatBalance balance,
        IRandomProvider rng)
    {
        var critChance = CombatMath.ComputeCritChance(attackerDerived, defenderDerived, balance);
        var critRoll = rng.NextDecimal(0, 1);
        return critRoll < critChance;
    }

    /// <summary>
    /// Rolls dodge chance and returns whether dodge occurred.
    /// Returns true if dodged, false if not.
    /// </summary>
    private static bool RollDodge(
        DerivedCombatStats attackerDerived,
        DerivedCombatStats defenderDerived,
        CombatBalance balance,
        IRandomProvider rng)
    {
        var dodgeChance = CombatMath.ComputeDodgeChance(attackerDerived, defenderDerived, balance);
        var dodgeRoll = rng.NextDecimal(0, 1);
        return dodgeRoll < dodgeChance;
    }

}



