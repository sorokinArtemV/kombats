namespace Kombats.Battle.Domain.Rules;

/// <summary>
/// Derived combat stats computed from base PlayerStats and CombatBalance.
/// These are computed once and used for combat calculations.
/// </summary>
public sealed record DerivedCombatStats
{
    public int HpMax { get; init; }
    public int DamageMin { get; init; }
    public int DamageMax { get; init; }
    public int MfDodge { get; init; }
    public int MfAntiDodge { get; init; }
    public int MfCrit { get; init; }
    public int MfAntiCrit { get; init; }

    public DerivedCombatStats(
        int hpMax,
        int damageMin,
        int damageMax,
        int mfDodge,
        int mfAntiDodge,
        int mfCrit,
        int mfAntiCrit)
    {
        if (hpMax < 0)
            throw new ArgumentException("HpMax cannot be negative", nameof(hpMax));
        if (damageMin < 0)
            throw new ArgumentException("DamageMin cannot be negative", nameof(damageMin));
        if (damageMax < 0)
            throw new ArgumentException("DamageMax cannot be negative", nameof(damageMax));
        if (damageMin > damageMax)
            throw new ArgumentException("DamageMin must be less than or equal to DamageMax", nameof(damageMin));
        if (mfDodge < 0)
            throw new ArgumentException("MfDodge cannot be negative", nameof(mfDodge));
        if (mfAntiDodge < 0)
            throw new ArgumentException("MfAntiDodge cannot be negative", nameof(mfAntiDodge));
        if (mfCrit < 0)
            throw new ArgumentException("MfCrit cannot be negative", nameof(mfCrit));
        if (mfAntiCrit < 0)
            throw new ArgumentException("MfAntiCrit cannot be negative", nameof(mfAntiCrit));

        HpMax = hpMax;
        DamageMin = damageMin;
        DamageMax = damageMax;
        MfDodge = mfDodge;
        MfAntiDodge = mfAntiDodge;
        MfCrit = mfCrit;
        MfAntiCrit = mfAntiCrit;
    }
}


