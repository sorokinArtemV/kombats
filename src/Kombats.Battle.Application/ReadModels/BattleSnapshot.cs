using Kombats.Battle.Domain.Model;
using Kombats.Battle.Domain.Rules;

namespace Kombats.Battle.Application.ReadModels;

/// <summary>
/// Read model for battle state - used for UI queries and realtime notifications.
/// This is a read-only snapshot optimized for presentation.
/// </summary>
public sealed class BattleSnapshot
{
    public Guid BattleId { get; init; }
    public Guid MatchId { get; init; }
    public Guid PlayerAId { get; init; }
    public Guid PlayerBId { get; init; }
    public Ruleset Ruleset { get; init; } = null!;
    public BattlePhase Phase { get; init; }
    public int TurnIndex { get; init; }
    public DateTime DeadlineUtc { get; init; }
    public int NoActionStreakBoth { get; init; }
    public int LastResolvedTurnIndex { get; init; }
    public int Version { get; init; }
    
    // Player HP
    public int? PlayerAHp { get; init; }
    public int? PlayerBHp { get; init; }
    
    // Player stats
    public int? PlayerAStrength { get; init; }
    public int? PlayerAStamina { get; init; }
    public int? PlayerAAgility { get; init; }
    public int? PlayerAIntuition { get; init; }
    public int? PlayerBStrength { get; init; }
    public int? PlayerBStamina { get; init; }
    public int? PlayerBAgility { get; init; }
    public int? PlayerBIntuition { get; init; }
}





