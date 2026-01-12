namespace Kombats.Battle.Infrastructure.Persistence.EF.Entities;

public class BattleEntity
{
    public Guid BattleId { get; set; }
    public Guid MatchId { get; set; }
    public Guid PlayerAId { get; set; }
    public Guid PlayerBId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string? EndReason { get; set; }
    public Guid? WinnerPlayerId { get; set; }
}









