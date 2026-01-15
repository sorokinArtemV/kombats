namespace Kombats.Battle.Domain.Model;

/// <summary>
/// Battle phase enum (domain-level, matches infrastructure enum).
/// </summary>
public enum BattlePhase
{
    ArenaOpen = 0,
    TurnOpen = 1,
    Resolving = 2,
    Ended = 3
}