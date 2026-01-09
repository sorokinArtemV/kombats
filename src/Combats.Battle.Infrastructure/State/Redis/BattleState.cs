using System.Text.Json.Serialization;
using Combats.Battle.Domain.Model;
using Combats.Battle.Domain.Rules;

namespace Combats.Battle.Infrastructure.State.Redis;

/// <summary>
/// Infrastructure-specific battle state (Redis JSON serialization).
/// This is the concrete type stored in Redis.
/// Uses domain models for Ruleset and BattlePhase.
/// </summary>
public class BattleState
{
    public Guid BattleId { get; set; }
    public Guid PlayerAId { get; set; }
    public Guid PlayerBId { get; set; }
    public Ruleset Ruleset { get; set; } = null!;
    public BattlePhase Phase { get; set; }
    public int TurnIndex { get; set; }
    
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long DeadlineUtcTicks { get; set; }
    
    public int NoActionStreakBoth { get; set; }
    public int LastResolvedTurnIndex { get; set; }
    public Guid MatchId { get; set; } // Store MatchId for BattleEnded event
    public int Version { get; set; } = 1;

    // Player HP (for battle engine)
    public int? PlayerAHp { get; set; }
    public int? PlayerBHp { get; set; }
    
    // Player stats (for fistfight combat)
    public int? PlayerAStrength { get; set; }
    public int? PlayerAStamina { get; set; }
    public int? PlayerBStrength { get; set; }
    public int? PlayerBStamina { get; set; }

    // Helper methods for DateTime conversion
    public DateTime GetDeadlineUtc() => new DateTime(DeadlineUtcTicks, DateTimeKind.Utc);
    
    public void SetDeadlineUtc(DateTime deadlineUtc)
    {
        DeadlineUtcTicks = deadlineUtc.ToUniversalTime().Ticks;
    }
}

// BattlePhase is now defined in Domain.Model.BattlePhase


