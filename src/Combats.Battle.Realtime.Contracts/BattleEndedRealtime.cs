namespace Combats.Battle.Realtime.Contracts;

/// <summary>
/// Realtime contract for BattleEnded event.
/// </summary>
public record BattleEndedRealtime
{
    public Guid BattleId { get; init; }
    public BattleEndReasonRealtime Reason { get; init; }
    public Guid? WinnerPlayerId { get; init; }
    public DateTimeOffset EndedAt { get; init; }
}





