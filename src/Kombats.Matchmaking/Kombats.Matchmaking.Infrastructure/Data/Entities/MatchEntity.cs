namespace Kombats.Matchmaking.Infrastructure.Data.Entities;

/// <summary>
/// EF Core entity for Match storage.
/// </summary>
public class MatchEntity
{
    public Guid MatchId { get; set; }
    public Guid BattleId { get; set; }
    public Guid PlayerAId { get; set; }
    public Guid PlayerBId { get; set; }
    public string Variant { get; set; } = string.Empty;
    public int State { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}




