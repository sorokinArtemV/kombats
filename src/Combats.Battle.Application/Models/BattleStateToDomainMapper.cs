using Combats.Battle.Application.Ports;
using Combats.Battle.Domain;
using Combats.Battle.Domain.Model;

namespace Combats.Battle.Application.Models;

/// <summary>
/// Mapper between Application BattleStateView and Domain BattleDomainState.
/// Application layer owns this mapping (Application View -> Domain State).
/// </summary>
public static class BattleStateToDomainMapper
{
    /// <summary>
    /// Maps Application BattleStateView to Domain BattleDomainState.
    /// </summary>
    public static BattleDomainState ToDomainState(BattleStateView state)
    {
        // Get player stats (defaults if not set)
        var playerAStrength = state.PlayerAStrength ?? 10;
        var playerAStamina = state.PlayerAStamina ?? 10;
        var playerBStrength = state.PlayerBStrength ?? 10;
        var playerBStamina = state.PlayerBStamina ?? 10;
        
        // Calculate max HP from stamina
        var playerAMaxHp = playerAStamina * (state.Ruleset.HpPerStamina > 0 ? state.Ruleset.HpPerStamina : 10);
        var playerBMaxHp = playerBStamina * (state.Ruleset.HpPerStamina > 0 ? state.Ruleset.HpPerStamina : 10);
        
        // Get current HP (or max if not set)
        var playerAHp = state.PlayerAHp ?? playerAMaxHp;
        var playerBHp = state.PlayerBHp ?? playerBMaxHp;

        var playerAStats = new PlayerStats(playerAStrength, playerAStamina);
        var playerBStats = new PlayerStats(playerBStrength, playerBStamina);
        
        var playerA = new PlayerState(state.PlayerAId, playerAMaxHp, playerAHp, playerAStats);
        var playerB = new PlayerState(state.PlayerBId, playerBMaxHp, playerBHp, playerBStats);

        // Map phase enum
        var domainPhase = state.Phase switch
        {
            BattlePhaseView.ArenaOpen => BattlePhase.ArenaOpen,
            BattlePhaseView.TurnOpen => BattlePhase.TurnOpen,
            BattlePhaseView.Resolving => BattlePhase.Resolving,
            BattlePhaseView.Ended => BattlePhase.Ended,
            _ => throw new ArgumentException($"Unknown phase: {state.Phase}")
        };

        return new BattleDomainState(
            state.BattleId,
            state.MatchId,
            state.PlayerAId,
            state.PlayerBId,
            state.Ruleset,
            domainPhase,
            state.TurnIndex,
            state.NoActionStreakBoth,
            state.LastResolvedTurnIndex,
            playerA,
            playerB);
    }
}

