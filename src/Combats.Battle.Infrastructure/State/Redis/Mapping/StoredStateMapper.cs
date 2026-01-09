using Combats.Battle.Application.ReadModels;
using Combats.Battle.Domain.Model;
using Combats.Battle.Infrastructure.State.Redis;

namespace Combats.Battle.Infrastructure.State.Redis.Mapping;

/// <summary>
/// Mapper between Infrastructure stored state (Redis schema) and Domain/Application models.
/// Infrastructure layer owns this mapping (persistence schema <-> domain/read models).
/// </summary>
public static class StoredStateMapper
{
    /// <summary>
    /// Maps Infrastructure BattleState to Application BattleSnapshot (read model).
    /// </summary>
    public static BattleSnapshot ToSnapshot(BattleState state)
    {
        return new BattleSnapshot
        {
            BattleId = state.BattleId,
            PlayerAId = state.PlayerAId,
            PlayerBId = state.PlayerBId,
            Ruleset = state.Ruleset,
            Phase = state.Phase,
            TurnIndex = state.TurnIndex,
            DeadlineUtc = state.GetDeadlineUtc(),
            NoActionStreakBoth = state.NoActionStreakBoth,
            LastResolvedTurnIndex = state.LastResolvedTurnIndex,
            MatchId = state.MatchId,
            Version = state.Version,
            PlayerAHp = state.PlayerAHp,
            PlayerBHp = state.PlayerBHp,
            PlayerAStrength = state.PlayerAStrength,
            PlayerAStamina = state.PlayerAStamina,
            PlayerAAgility = state.PlayerAAgility,
            PlayerAIntuition = state.PlayerAIntuition,
            PlayerBStrength = state.PlayerBStrength,
            PlayerBStamina = state.PlayerBStamina,
            PlayerBAgility = state.PlayerBAgility,
            PlayerBIntuition = state.PlayerBIntuition
        };
    }

    /// <summary>
    /// Maps Domain BattleDomainState to Infrastructure BattleState (for storage).
    /// </summary>
    public static BattleState FromDomainState(BattleDomainState domainState, DateTime deadlineUtc, int version)
    {
        var state = new BattleState
        {
            BattleId = domainState.BattleId,
            PlayerAId = domainState.PlayerAId,
            PlayerBId = domainState.PlayerBId,
            Ruleset = domainState.Ruleset,
            Phase = domainState.Phase,
            TurnIndex = domainState.TurnIndex,
            NoActionStreakBoth = domainState.NoActionStreakBoth,
            LastResolvedTurnIndex = domainState.LastResolvedTurnIndex,
            MatchId = domainState.MatchId,
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
        state.SetDeadlineUtc(deadlineUtc);
        return state;
    }
}


