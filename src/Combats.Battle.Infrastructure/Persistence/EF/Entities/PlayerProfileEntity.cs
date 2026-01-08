namespace Combats.Battle.Infrastructure.Persistence.EF.Entities;

/// <summary>
/// Entity for player combat profile (stats snapshot).
/// This is a read model/projection that can be updated from character service events.
/// </summary>
public class PlayerProfileEntity
{
    public Guid PlayerId { get; set; }
    public int Strength { get; set; }
    public int Stamina { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
}





