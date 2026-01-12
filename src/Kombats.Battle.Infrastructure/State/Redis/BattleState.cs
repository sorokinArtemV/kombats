using System.Text.Json.Serialization;
using Kombats.Battle.Domain.Model;
using Kombats.Battle.Domain.Rules;

namespace Kombats.Battle.Infrastructure.State.Redis;

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
    
    /// <summary>
    /// Turn deadline in unix milliseconds (Int64).
    /// Stored in Redis JSON and used as ZSET score for consistency.
    /// </summary>
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long DeadlineUnixMs { get; set; }
    
    public int NoActionStreakBoth { get; set; }
    public int LastResolvedTurnIndex { get; set; }
    public Guid MatchId { get; set; } // Store MatchId for BattleEnded event
    public int Version { get; set; } = 1;

    // Player HP (for battle engine)
    public int? PlayerAHp { get; set; }
    public int? PlayerBHp { get; set; }
    
    // Player stats (for combat)
    public int? PlayerAStrength { get; set; }
    public int? PlayerAStamina { get; set; }
    public int? PlayerAAgility { get; set; }
    public int? PlayerAIntuition { get; set; }
    public int? PlayerBStrength { get; set; }
    public int? PlayerBStamina { get; set; }
    public int? PlayerBAgility { get; set; }
    public int? PlayerBIntuition { get; set; }

    // Helper methods for DateTime conversion
    /// <summary>
    /// Converts DeadlineUnixMs to DateTime (UTC).
    /// </summary>
    public DateTime GetDeadlineUtc() => DateTimeOffset.FromUnixTimeMilliseconds(DeadlineUnixMs).UtcDateTime;
    
    /// <summary>
    /// Sets DeadlineUnixMs from DateTime (UTC).
    /// </summary>
    public void SetDeadlineUtc(DateTime deadlineUtc)
    {
        var deadlineOffset = new DateTimeOffset(deadlineUtc.ToUniversalTime(), TimeSpan.Zero);
        DeadlineUnixMs = deadlineOffset.ToUnixTimeMilliseconds();
    }
}

// BattlePhase is now defined in Domain.Model.BattlePhase


