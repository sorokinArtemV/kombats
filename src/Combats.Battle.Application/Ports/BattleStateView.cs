using Combats.Contracts.Battle;

namespace Combats.Battle.Application.Ports;

/// <summary>
/// Application view of battle state (read-only snapshot).
/// Infrastructure maps its BattleState to/from this view.
/// </summary>
public class BattleStateView
{
    public Guid BattleId { get; init; }
    public Guid PlayerAId { get; init; }
    public Guid PlayerBId { get; init; }
    public Ruleset Ruleset { get; init; } = null!;
    public BattlePhaseView Phase { get; init; }
    public int TurnIndex { get; init; }
    public DateTime DeadlineUtc { get; init; }
    public int NoActionStreakBoth { get; init; }
    public int LastResolvedTurnIndex { get; init; }
    public Guid MatchId { get; init; }
    public int Version { get; init; }
    
    // Player HP
    public int? PlayerAHp { get; init; }
    public int? PlayerBHp { get; init; }
    
    // Player stats
    public int? PlayerAStrength { get; init; }
    public int? PlayerAStamina { get; init; }
    public int? PlayerBStrength { get; init; }
    public int? PlayerBStamina { get; init; }
}

/// <summary>
/// Battle phase enum for Application layer (matches Domain enum).
/// </summary>
public enum BattlePhaseView
{
    ArenaOpen = 0,
    TurnOpen = 1,
    Resolving = 2,
    Ended = 3
}



