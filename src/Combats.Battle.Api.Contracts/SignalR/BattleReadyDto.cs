namespace Combats.Battle.Api.Contracts.SignalR;

public class BattleReadyDto
{
    public Guid BattleId { get; init; }
    public Guid PlayerAId { get; init; }
    public Guid PlayerBId { get; init; }
}



