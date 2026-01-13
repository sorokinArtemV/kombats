namespace Kombats.Contracts.Battle;

/// <summary>
/// Command to create a new battle.
/// Matchmaking does NOT provide ruleset - Battle service selects from configuration.
/// </summary>
public record CreateBattle
{
    public Guid BattleId { get; init; }
    public Guid MatchId { get; init; }
    public Guid PlayerAId { get; init; }
    public Guid PlayerBId { get; init; }
    public DateTime RequestedAt { get; init; }
}






