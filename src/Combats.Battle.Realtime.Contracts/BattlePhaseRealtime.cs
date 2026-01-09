namespace Combats.Battle.Realtime.Contracts;

/// <summary>
/// Battle phase enum for realtime contracts.
/// Maps to Domain.Model.BattlePhase but kept separate to maintain boundary independence.
/// </summary>
public enum BattlePhaseRealtime
{
    ArenaOpen = 0,
    TurnOpen = 1,
    Resolving = 2,
    Ended = 3
}





