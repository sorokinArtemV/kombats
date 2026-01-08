namespace Combats.Battle.Realtime.Contracts;

/// <summary>
/// Realtime contract for BattleReady event.
/// </summary>
public record BattleReadyRealtime
{
    public Guid BattleId { get; init; }
    public Guid PlayerAId { get; init; }
    public Guid PlayerBId { get; init; }
}

