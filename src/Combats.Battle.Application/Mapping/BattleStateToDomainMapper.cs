using Combats.Battle.Application.ReadModels;
using Combats.Battle.Domain.Model;
using Combats.Battle.Domain.Rules;

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
        var playerAAgility = snapshot.PlayerAAgility ?? 0;
        var playerAIntuition = snapshot.PlayerAIntuition ?? 0;
        var playerBStrength = snapshot.PlayerBStrength ?? 10;
        var playerBStamina = snapshot.PlayerBStamina ?? 10;
        var playerBAgility = snapshot.PlayerBAgility ?? 0;
        var playerBIntuition = snapshot.PlayerBIntuition ?? 0;
        
        var playerAStats = new PlayerStats(playerAStrength, playerAStamina, playerAAgility, playerAIntuition);
        var playerBStats = new PlayerStats(playerBStrength, playerBStamina, playerBAgility, playerBIntuition);
        
        // Compute HP using CombatMath (derived stats)
        var derivedA = CombatMath.ComputeDerived(playerAStats, snapshot.Ruleset.Balance);
        var derivedB = CombatMath.ComputeDerived(playerBStats, snapshot.Ruleset.Balance);
        
        // Get current HP (or max if not set)
        var playerAHp = snapshot.PlayerAHp ?? derivedA.HpMax;
        var playerBHp = snapshot.PlayerBHp ?? derivedB.HpMax;
        
        var playerA = new PlayerState(snapshot.PlayerAId, derivedA.HpMax, playerAHp, playerAStats);
        var playerB = new PlayerState(snapshot.PlayerBId, derivedB.HpMax, playerBHp, playerBStats);

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


