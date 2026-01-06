namespace Combats.Battle.Api.Contracts.SignalR;

public class BattleEndedDto
{
    public Guid BattleId { get; init; }
    public string Reason { get; init; } = string.Empty;
    public Guid? WinnerPlayerId { get; init; }
    public string EndedAt { get; init; } = string.Empty; // ISO 8601 string
}


