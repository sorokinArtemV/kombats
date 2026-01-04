using System.Text.Json.Serialization;
using Combats.Contracts.Battle;

namespace Combats.Services.Battle.State;

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
    
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long NextResolveScheduledUtcTicks { get; set; } // Tracks when ResolveTurn was last scheduled (for watchdog recovery)
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

    public DateTime? GetNextResolveScheduledUtc()
    {
        return NextResolveScheduledUtcTicks > 0 
            ? new DateTime(NextResolveScheduledUtcTicks, DateTimeKind.Utc) 
            : null;
    }

    public void SetNextResolveScheduledUtc(DateTime scheduledUtc)
    {
        NextResolveScheduledUtcTicks = scheduledUtc.ToUniversalTime().Ticks;
    }
}

