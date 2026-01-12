namespace Kombats.Contracts.Battle;

public record CreateBattle
{
    public Guid BattleId { get; init; }
    public Guid MatchId { get; init; }
    public Guid PlayerAId { get; init; }
    public Guid PlayerBId { get; init; }
    public RulesetDto RulesetDto { get; init; } = null!;
    public DateTime RequestedAt { get; init; }
}

public record RulesetDto
{
    public int Version { get; init; }
    public int TurnSeconds { get; init; }
    public int NoActionLimit { get; init; }
    public int Seed { get; init; }
}






