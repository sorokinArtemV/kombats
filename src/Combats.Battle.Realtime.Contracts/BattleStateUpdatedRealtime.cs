namespace Combats.Battle.Realtime.Contracts;

/// <summary>
/// Realtime contract for BattleStateUpdated event.
/// </summary>
public record BattleStateUpdatedRealtime
{
    public Guid BattleId { get; init; }
    public Guid PlayerAId { get; init; }
    public Guid PlayerBId { get; init; }
    public BattleRulesetRealtime Ruleset { get; init; } = null!;
    public BattlePhaseRealtime Phase { get; init; }
    public int TurnIndex { get; init; }
    public DateTimeOffset DeadlineUtc { get; init; }
    public int NoActionStreakBoth { get; init; }
    public int LastResolvedTurnIndex { get; init; }
    public BattleEndReasonRealtime? EndedReason { get; init; }
    public int Version { get; init; }
    public int? PlayerAHp { get; init; }
    public int? PlayerBHp { get; init; }
}


