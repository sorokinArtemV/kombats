using Combats.Contracts.Battle;

namespace Combats.Battle.Api.Contracts.SignalR;

public class BattleSnapshotDto
{
    public Guid BattleId { get; init; }
    public Guid PlayerAId { get; init; }
    public Guid PlayerBId { get; init; }
    public Ruleset Ruleset { get; init; } = null!;
    public string Phase { get; init; } = string.Empty;
    public int TurnIndex { get; init; }
    public string DeadlineUtc { get; init; } = string.Empty; // ISO 8601 string
    public int NoActionStreakBoth { get; init; }
    public int LastResolvedTurnIndex { get; init; }
    public string? EndedReason { get; init; } // null if not ended, otherwise reason string
    public int Version { get; init; }
    public int? PlayerAHp { get; init; }
    public int? PlayerBHp { get; init; }
}


