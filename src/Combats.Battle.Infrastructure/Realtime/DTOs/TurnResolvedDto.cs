namespace Combats.Battle.Infrastructure.Realtime.DTOs;

/// <summary>
/// DTO for TurnResolved SignalR event.
/// </summary>
public class TurnResolvedDto
{
    public Guid BattleId { get; init; }
    public int TurnIndex { get; init; }
    public string PlayerAAction { get; init; } = string.Empty; // JSON or description
    public string PlayerBAction { get; init; } = string.Empty;
}


