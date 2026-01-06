namespace Combats.Battle.Domain;

/// <summary>
/// Represents the state of a player in a battle.
/// This is a domain model, independent of infrastructure.
/// </summary>
public sealed class PlayerState
{
    public Guid PlayerId { get; init; }
    public int MaxHp { get; init; }
    public int CurrentHp { get; set; } // Public set for mapper and engine
    public PlayerStats Stats { get; init; }

    public PlayerState(Guid playerId, int maxHp, PlayerStats stats)
    {
        PlayerId = playerId;
        MaxHp = maxHp;
        CurrentHp = maxHp;
        Stats = stats;
    }

    public PlayerState(Guid playerId, int maxHp, int currentHp, PlayerStats stats)
    {
        PlayerId = playerId;
        MaxHp = maxHp;
        CurrentHp = currentHp;
        Stats = stats;
    }

    public void ApplyDamage(int damage)
    {
        if (damage < 0)
            throw new ArgumentException("Damage cannot be negative", nameof(damage));

        CurrentHp = Math.Max(0, CurrentHp - damage);
    }

    public void Heal(int amount)
    {
        if (amount < 0)
            throw new ArgumentException("Heal amount cannot be negative", nameof(amount));

        CurrentHp = Math.Min(MaxHp, CurrentHp + amount);
    }

    public bool IsAlive => CurrentHp > 0;
    public bool IsDead => !IsAlive;
}


