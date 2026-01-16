namespace Kombats.Battle.Domain.Model;

/// <summary>
/// Player stats for combat.
/// Strength, Stamina (Endurance), Agility, and Intuition are used in combat rules.
/// </summary>
public sealed class PlayerStats
{
    public int Strength { get; init; }
    public int Stamina { get; init; }
    public int Agility { get; init; }
    public int Intuition { get; init; }

    public PlayerStats(int strength, int stamina, int agility, int intuition)
    {
        if (strength < 0)
            throw new ArgumentException("Strength cannot be negative", nameof(strength));
        if (stamina < 0)
            throw new ArgumentException("Stamina cannot be negative", nameof(stamina));
        if (agility < 0)
            throw new ArgumentException("Agility cannot be negative", nameof(agility));
        if (intuition < 0)
            throw new ArgumentException("Intuition cannot be negative", nameof(intuition));

        Strength = strength;
        Stamina = stamina;
        Agility = agility;
        Intuition = intuition;
    }
}


