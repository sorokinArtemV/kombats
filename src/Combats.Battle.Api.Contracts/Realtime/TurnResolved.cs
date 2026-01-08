namespace Combats.Battle.Api.Contracts.Realtime;

/// <summary>
/// DTO for TurnResolved realtime event.
/// </summary>
public class TurnResolved
{
    public Guid BattleId { get; init; }
    public int TurnIndex { get; init; }
    public string PlayerAAction { get; init; } = string.Empty; // JSON or description
    public string PlayerBAction { get; init; } = string.Empty;
}



