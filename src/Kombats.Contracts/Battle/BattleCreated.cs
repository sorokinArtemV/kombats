namespace Kombats.Contracts.Battle;

public record BattleCreated
{
    public Guid BattleId { get; init; }
    public Guid MatchId { get; init; }
    public Guid PlayerAId { get; init; }
    public Guid PlayerBId { get; init; }
    public Ruleset Ruleset { get; init; } = null!;
    public string State { get; init; } = string.Empty;
    public string? BattleServer { get; init; }
    public DateTime CreatedAt { get; init; }
    public int Version { get; init; }
}



