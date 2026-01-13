namespace Combats.Battle.Realtime.Contracts;

/// <summary>
/// Realtime contract for TurnResolved event.
/// </summary>
public record TurnResolvedRealtime
{
    public Guid BattleId { get; init; }
    public int TurnIndex { get; init; }
    public string PlayerAAction { get; init; } = string.Empty;
    public string PlayerBAction { get; init; } = string.Empty;
    /// <summary>
    /// Optional detailed combat resolution log. Present when server supports detailed outcomes.
    /// </summary>
    public TurnResolutionLogRealtime? Log { get; init; }
}






