namespace Kombats.Contracts.Battle;

/// <summary>
/// Event published when a battle is created.
/// Includes ruleset version and seed for reference (output only, not input).
/// </summary>
public record BattleCreated
{
    public Guid BattleId { get; init; }
    public Guid MatchId { get; init; }
    public Guid PlayerAId { get; init; }
    public Guid PlayerBId { get; init; }
    public int RulesetVersion { get; init; }
    public int Seed { get; init; }
    public string State { get; init; } = string.Empty;
    public string? BattleServer { get; init; }
    public DateTime CreatedAt { get; init; }
    public int Version { get; init; }
}



