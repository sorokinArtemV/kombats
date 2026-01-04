using Combats.Services.Battle.State;

namespace Combats.Services.Battle.Domain;

/// <summary>
/// Mapper between infrastructure BattleState (Redis) and domain BattleDomainState.
/// This isolates domain from infrastructure concerns.
/// </summary>
public static class BattleStateMapper
{
    private const int DefaultInitialHp = 100;

    /// <summary>
    /// Maps infrastructure BattleState to domain BattleDomainState.
    /// </summary>
    public static BattleDomainState ToDomainState(BattleState state)
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
            State.BattlePhase.ArenaOpen => BattlePhase.ArenaOpen,
            State.BattlePhase.TurnOpen => BattlePhase.TurnOpen,
            State.BattlePhase.Resolving => BattlePhase.Resolving,
            State.BattlePhase.Ended => BattlePhase.Ended,
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

    /// <summary>
    /// Maps domain BattleDomainState back to infrastructure BattleState.
    /// </summary>
    public static BattleState ToInfrastructureState(BattleDomainState domainState, BattleState? existingState = null)
    {
        var state = existingState ?? new BattleState
        {
            BattleId = domainState.BattleId,
            MatchId = domainState.MatchId,
            PlayerAId = domainState.PlayerAId,
            PlayerBId = domainState.PlayerBId,
            Ruleset = domainState.Ruleset,
            Version = 1
        };

        // Map phase enum
        state.Phase = domainState.Phase switch
        {
            BattlePhase.ArenaOpen => State.BattlePhase.ArenaOpen,
            BattlePhase.TurnOpen => State.BattlePhase.TurnOpen,
            BattlePhase.Resolving => State.BattlePhase.Resolving,
            BattlePhase.Ended => State.BattlePhase.Ended,
            _ => throw new ArgumentException($"Unknown domain phase: {domainState.Phase}")
        };

        state.TurnIndex = domainState.TurnIndex;
        state.NoActionStreakBoth = domainState.NoActionStreakBoth;
        state.LastResolvedTurnIndex = domainState.LastResolvedTurnIndex;

        // Store HP and stats in state
        state.PlayerAHp = domainState.PlayerA.CurrentHp;
        state.PlayerBHp = domainState.PlayerB.CurrentHp;
        state.PlayerAStrength = domainState.PlayerA.Stats.Strength;
        state.PlayerAStamina = domainState.PlayerA.Stats.Stamina;
        state.PlayerBStrength = domainState.PlayerB.Stats.Strength;
        state.PlayerBStamina = domainState.PlayerB.Stats.Stamina;

        return state;
    }
}

