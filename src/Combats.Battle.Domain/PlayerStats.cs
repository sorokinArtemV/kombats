namespace Combats.Battle.Domain;

/// <summary>
/// Player stats for fistfight combat.
/// Only Strength and Stamina are used in fistfight rules.
/// </summary>
public sealed class PlayerStats
{
    public int Strength { get; init; }
    public int Stamina { get; init; }

    public PlayerStats(int strength, int stamina)
    {
        if (strength < 0)
            throw new ArgumentException("Strength cannot be negative", nameof(strength));
        if (stamina < 0)
            throw new ArgumentException("Stamina cannot be negative", nameof(stamina));

        Strength = strength;
        Stamina = stamina;
    }
}


