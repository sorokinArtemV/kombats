using Combats.Battle.Application.Ports;
using Combats.Battle.Infrastructure.State;

namespace Combats.Battle.Infrastructure.Mapping;

/// <summary>
/// Mapper between Infrastructure BattleState and Application BattleStateView.
/// Infrastructure layer owns this mapping.
/// </summary>
public static class BattleStateMapper
{
    /// <summary>
    /// Maps Infrastructure BattleState to Application BattleStateView.
    /// </summary>
    public static BattleStateView ToView(BattleState state)
    {
        return new BattleStateView
        {
            BattleId = state.BattleId,
            PlayerAId = state.PlayerAId,
            PlayerBId = state.PlayerBId,
            Ruleset = state.Ruleset,
            Phase = MapPhase(state.Phase),
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
            PlayerBStrength = state.PlayerBStrength,
            PlayerBStamina = state.PlayerBStamina
        };
    }

    /// <summary>
    /// Maps Application BattleStateView to Infrastructure BattleState.
    /// </summary>
    public static BattleState FromView(BattleStateView view)
    {
        var state = new BattleState
        {
            BattleId = view.BattleId,
            PlayerAId = view.PlayerAId,
            PlayerBId = view.PlayerBId,
            Ruleset = view.Ruleset,
            Phase = MapPhase(view.Phase),
            TurnIndex = view.TurnIndex,
            NoActionStreakBoth = view.NoActionStreakBoth,
            LastResolvedTurnIndex = view.LastResolvedTurnIndex,
            MatchId = view.MatchId,
            Version = view.Version,
            PlayerAHp = view.PlayerAHp,
            PlayerBHp = view.PlayerBHp,
            PlayerAStrength = view.PlayerAStrength,
            PlayerAStamina = view.PlayerAStamina,
            PlayerBStrength = view.PlayerBStrength,
            PlayerBStamina = view.PlayerBStamina
        };
        state.SetDeadlineUtc(view.DeadlineUtc);
        return state;
    }

    private static BattlePhaseView MapPhase(BattlePhase phase)
    {
        return phase switch
        {
            BattlePhase.ArenaOpen => BattlePhaseView.ArenaOpen,
            BattlePhase.TurnOpen => BattlePhaseView.TurnOpen,
            BattlePhase.Resolving => BattlePhaseView.Resolving,
            BattlePhase.Ended => BattlePhaseView.Ended,
            _ => throw new ArgumentException($"Unknown phase: {phase}")
        };
    }

    private static BattlePhase MapPhase(BattlePhaseView phase)
    {
        return phase switch
        {
            BattlePhaseView.ArenaOpen => BattlePhase.ArenaOpen,
            BattlePhaseView.TurnOpen => BattlePhase.TurnOpen,
            BattlePhaseView.Resolving => BattlePhase.Resolving,
            BattlePhaseView.Ended => BattlePhase.Ended,
            _ => throw new ArgumentException($"Unknown phase: {phase}")
        };
    }
}


