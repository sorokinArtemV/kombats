namespace Kombats.Matchmaking.Domain;

/// <summary>
/// Match state enumeration.
/// </summary>
public enum MatchState
{
    Created = 0,
    BattleCreateRequested = 1,
    BattleCreated = 2,
    Completed = 3,
    TimedOut = 4
}

