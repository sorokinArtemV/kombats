namespace Combats.Battle.Api.Contracts.SignalR;

public class TurnOpenedDto
{
    public Guid BattleId { get; init; }
    public int TurnIndex { get; init; }
    public string DeadlineUtc { get; init; } = string.Empty; // ISO 8601 string
}



