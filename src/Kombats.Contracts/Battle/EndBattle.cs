namespace Kombats.Contracts.Battle;

public record EndBattle
{
    public Guid BattleId { get; init; }
    public Guid MatchId { get; init; }
    public BattleEndReason Reason { get; init; }
    public DateTime RequestedAt { get; init; }
    public int Version { get; init; } = 1;
}
