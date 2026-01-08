using Combats.Battle.Application.ReadModels;
using Combats.Battle.Domain.Model;

namespace Combats.Battle.Application.Mapping;

/// <summary>
/// Mapper between Application read models and Domain models.
/// </summary>
public static class BattleStateToDomainMapper
{
    /// <summary>
    /// Maps Application BattleSnapshot to Domain BattleDomainState.
    /// </summary>
    public static BattleDomainState ToDomainState(BattleSnapshot snapshot)
    {
        // Get player stats (defaults if not set)
        var playerAStrength = snapshot.PlayerAStrength ?? 10;
        var playerAStamina = snapshot.PlayerAStamina ?? 10;
        var playerBStrength = snapshot.PlayerBStrength ?? 10;
        var playerBStamina = snapshot.PlayerBStamina ?? 10;
        
        // Calculate max HP from stamina
        var playerAMaxHp = playerAStamina * snapshot.Ruleset.HpPerStamina;
        var playerBMaxHp = playerBStamina * snapshot.Ruleset.HpPerStamina;
        
        // Get current HP (or max if not set)
        var playerAHp = snapshot.PlayerAHp ?? playerAMaxHp;
        var playerBHp = snapshot.PlayerBHp ?? playerBMaxHp;

        var playerAStats = new PlayerStats(playerAStrength, playerAStamina);
        var playerBStats = new PlayerStats(playerBStrength, playerBStamina);
        
        var playerA = new PlayerState(snapshot.PlayerAId, playerAMaxHp, playerAHp, playerAStats);
        var playerB = new PlayerState(snapshot.PlayerBId, playerBMaxHp, playerBHp, playerBStats);

        return new BattleDomainState(
            snapshot.BattleId,
            snapshot.MatchId,
            snapshot.PlayerAId,
            snapshot.PlayerBId,
            snapshot.Ruleset,
            snapshot.Phase,
            snapshot.TurnIndex,
            snapshot.NoActionStreakBoth,
            snapshot.LastResolvedTurnIndex,
            playerA,
            playerB);
    }
}


