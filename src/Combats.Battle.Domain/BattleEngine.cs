using Combats.Contracts.Battle;

namespace Combats.Battle.Domain;

/// <summary>
/// Battle engine implementation - pure domain logic for resolving turns in fistfight combat.
/// This is a pure function with no infrastructure dependencies.
/// </summary>
public sealed class BattleEngine : IBattleEngine
{
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
                    BattleEndReason.DoubleForfeit,
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
            playerA.Stats.Strength,
            state.Ruleset);

        // Damage from B to A
        int damageToA = CalculateDamage(
            normalizedActionB,
            normalizedActionA,
            playerB.Stats.Strength,
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
        var endReason = BattleEndReason.Normal;

        if (playerA.IsDead && playerB.IsDead)
        {
            // Both dead - draw
            endReason = BattleEndReason.Normal; // Could be a separate "Draw" reason
            winnerId = null;
        }
        else if (playerA.IsDead)
        {
            winnerId = state.PlayerBId;
            endReason = BattleEndReason.Normal;
        }
        else if (playerB.IsDead)
        {
            winnerId = state.PlayerAId;
            endReason = BattleEndReason.Normal;
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
    /// Returns 0 if attacker is NoAction or attack is blocked.
    /// </summary>
    private static int CalculateDamage(
        PlayerAction attackerAction,
        PlayerAction defenderAction,
        int attackerStrength,
        Ruleset ruleset)
    {
        // If attacker is NoAction, no damage
        if (attackerAction.IsNoAction || attackerAction.AttackZone == null)
        {
            return 0;
        }

        // Check if attack is blocked
        var attackZone = attackerAction.AttackZone.Value;
        var isBlocked = BattleZoneHelper.IsZoneBlocked(
            attackZone,
            defenderAction.BlockZonePrimary,
            defenderAction.BlockZoneSecondary);

        if (isBlocked)
        {
            // Attack is blocked - no damage
            return 0;
        }

        // Calculate damage from strength
        var damagePerStrength = ruleset.DamagePerStrength > 0 ? ruleset.DamagePerStrength : 2;
        return attackerStrength * damagePerStrength;
    }
}


