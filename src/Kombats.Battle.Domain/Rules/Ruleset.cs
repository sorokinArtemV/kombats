namespace Kombats.Battle.Domain.Rules;

/// <summary>
/// Battle ruleset - a domain value object that defines battle parameters.
/// This is the canonical source of truth for battle rules in the domain.
/// </summary>
public sealed record Ruleset
{
    public int Version { get; init; }
    public int TurnSeconds { get; init; }
    public int NoActionLimit { get; init; }
    public int Seed { get; init; }
    
    // Legacy fistfight combat parameters (kept for backward compatibility)
    public int HpPerStamina { get; init; } = 10; // Default: 1 Stamina = 10 HP
    public int DamagePerStrength { get; init; } = 2; // Default: 1 Strength = 2 damage

    // New combat balance system
    public CombatBalance Balance { get; init; } = null!;

    public Ruleset(
        int version,
        int turnSeconds,
        int noActionLimit,
        int seed,
        int hpPerStamina = 10,
        int damagePerStrength = 2,
        CombatBalance? balance = null)
    {
        if (turnSeconds <= 0)
            throw new ArgumentException("TurnSeconds must be positive", nameof(turnSeconds));
        if (noActionLimit <= 0)
            throw new ArgumentException("NoActionLimit must be positive", nameof(noActionLimit));
        if (hpPerStamina <= 0)
            throw new ArgumentException("HpPerStamina must be positive", nameof(hpPerStamina));
        if (damagePerStrength <= 0)
            throw new ArgumentException("DamagePerStrength must be positive", nameof(damagePerStrength));

        Version = version;
        TurnSeconds = turnSeconds;
        NoActionLimit = noActionLimit;
        Seed = seed;
        HpPerStamina = hpPerStamina;
        DamagePerStrength = damagePerStrength;
        Balance = balance ?? throw new ArgumentNullException(nameof(balance), "CombatBalance is required");
    }
}





