namespace Combats.Contracts.Battle;

public record BattleEnded
{
    public Guid BattleId { get; init; }
    public Guid MatchId { get; init; }
    public BattleEndReason Reason { get; init; }
    public Guid? WinnerPlayerId { get; init; }
    public DateTime EndedAt { get; init; }
    public int Version { get; init; } = 1;
}
