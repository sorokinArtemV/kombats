namespace Combats.Contracts.Battle;

public record CreateBattle
{
    public Guid BattleId { get; init; }
    public Guid MatchId { get; init; }
    public Guid PlayerAId { get; init; }
    public Guid PlayerBId { get; init; }
    public Ruleset Ruleset { get; init; } = null!;
    public DateTime RequestedAt { get; init; }
}

public record Ruleset
{
    public int Version { get; init; }
    public int TurnSeconds { get; init; }
    public int NoActionLimit { get; init; }
    public int Seed { get; init; }
    
    // Fistfight combat parameters
    public int HpPerStamina { get; init; } = 10; // Default: 1 Stamina = 10 HP
    public int DamagePerStrength { get; init; } = 2; // Default: 1 Strength = 2 damage
}






