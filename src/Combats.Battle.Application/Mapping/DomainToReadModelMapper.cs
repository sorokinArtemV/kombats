using Combats.Battle.Application.ReadModels;
using Combats.Battle.Domain.Model;

namespace Combats.Battle.Application.Mapping;

/// <summary>
/// Mapper from Domain models to Application read models.
/// </summary>
public static class DomainToReadModelMapper
{
    public static BattleSnapshot ToSnapshot(BattleDomainState domainState, DateTime deadlineUtc, int version)
    {
        return new BattleSnapshot
        {
            BattleId = domainState.BattleId,
            MatchId = domainState.MatchId,
            PlayerAId = domainState.PlayerAId,
            PlayerBId = domainState.PlayerBId,
            Ruleset = domainState.Ruleset,
            Phase = domainState.Phase,
            TurnIndex = domainState.TurnIndex,
            DeadlineUtc = deadlineUtc,
            NoActionStreakBoth = domainState.NoActionStreakBoth,
            LastResolvedTurnIndex = domainState.LastResolvedTurnIndex,
            Version = version,
            PlayerAHp = domainState.PlayerA.CurrentHp,
            PlayerBHp = domainState.PlayerB.CurrentHp,
            PlayerAStrength = domainState.PlayerA.Stats.Strength,
            PlayerAStamina = domainState.PlayerA.Stats.Stamina,
            PlayerAAgility = domainState.PlayerA.Stats.Agility,
            PlayerAIntuition = domainState.PlayerA.Stats.Intuition,
            PlayerBStrength = domainState.PlayerB.Stats.Strength,
            PlayerBStamina = domainState.PlayerB.Stats.Stamina,
            PlayerBAgility = domainState.PlayerB.Stats.Agility,
            PlayerBIntuition = domainState.PlayerB.Stats.Intuition
        };
    }
}





