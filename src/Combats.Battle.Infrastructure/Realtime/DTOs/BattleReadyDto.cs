namespace Combats.Battle.Infrastructure.Realtime.DTOs;

public class BattleReadyDto
{
    public Guid BattleId { get; init; }
    public Guid PlayerAId { get; init; }
    public Guid PlayerBId { get; init; }
}


