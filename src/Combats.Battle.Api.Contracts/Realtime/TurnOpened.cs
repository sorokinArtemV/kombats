namespace Combats.Battle.Api.Contracts.Realtime;

public class TurnOpened
{
    public Guid BattleId { get; init; }
    public int TurnIndex { get; init; }
    public string DeadlineUtc { get; init; } = string.Empty; // ISO 8601 string
}



