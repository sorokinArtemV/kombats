namespace Combats.Battle.Realtime.Contracts;

/// <summary>
/// Realtime contract for TurnOpened event.
/// </summary>
public record TurnOpenedRealtime
{
    public Guid BattleId { get; init; }
    public int TurnIndex { get; init; }
    public DateTimeOffset DeadlineUtc { get; init; }
}





