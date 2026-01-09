namespace Combats.Battle.Realtime.Contracts;

/// <summary>
/// Realtime contract for PlayerDamaged event.
/// </summary>
public record PlayerDamagedRealtime
{
    public Guid BattleId { get; init; }
    public Guid PlayerId { get; init; }
    public int Damage { get; init; }
    public int RemainingHp { get; init; }
    public int TurnIndex { get; init; }
}





