namespace Combats.Battle.Api.Contracts.Realtime;

public class BattleReady
{
    public Guid BattleId { get; init; }
    public Guid PlayerAId { get; init; }
    public Guid PlayerBId { get; init; }
}

