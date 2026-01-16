namespace Kombats.Battle.Domain.Rules;

/// <summary>
/// Immutable combat balance configuration - all balance parameters for combat calculations.
/// This is a domain value object that must be populated from Infrastructure configuration.
/// </summary>
public sealed record CombatBalance
{
    public HpBalance Hp { get; init; }
    public DamageBalance Damage { get; init; }
    public MfBalance Mf { get; init; }
    public ChanceBalance DodgeChance { get; init; }
    public ChanceBalance CritChance { get; init; }
    public CritEffectBalance CritEffect { get; init; }

    public CombatBalance(
        HpBalance hp,
        DamageBalance damage,
        MfBalance mf,
        ChanceBalance dodgeChance,
        ChanceBalance critChance,
        CritEffectBalance critEffect)
    {
        Hp = hp ?? throw new ArgumentNullException(nameof(hp));
        Damage = damage ?? throw new ArgumentNullException(nameof(damage));
        Mf = mf ?? throw new ArgumentNullException(nameof(mf));
        DodgeChance = dodgeChance ?? throw new ArgumentNullException(nameof(dodgeChance));
        CritChance = critChance ?? throw new ArgumentNullException(nameof(critChance));
        CritEffect = critEffect ?? throw new ArgumentNullException(nameof(critEffect));
    }
}

public sealed record HpBalance
{
    public int BaseHp { get; init; }
    public int HpPerEnd { get; init; }

    public HpBalance(int baseHp, int hpPerEnd)
    {
        if (baseHp < 0)
            throw new ArgumentException("BaseHp cannot be negative", nameof(baseHp));
        if (hpPerEnd <= 0)
            throw new ArgumentException("HpPerEnd must be positive", nameof(hpPerEnd));

        BaseHp = baseHp;
        HpPerEnd = hpPerEnd;
    }
}

public sealed record DamageBalance
{
    public int BaseWeaponDamage { get; init; }
    public decimal DamagePerStr { get; init; }
    public decimal DamagePerAgi { get; init; }
    public decimal DamagePerInt { get; init; }
    public decimal SpreadMin { get; init; }
    public decimal SpreadMax { get; init; }

    public DamageBalance(
        int baseWeaponDamage,
        decimal damagePerStr,
        decimal damagePerAgi,
        decimal damagePerInt,
        decimal spreadMin,
        decimal spreadMax)
    {
        if (baseWeaponDamage < 0)
            throw new ArgumentException("BaseWeaponDamage cannot be negative", nameof(baseWeaponDamage));
        if (damagePerStr < 0)
            throw new ArgumentException("DamagePerStr cannot be negative", nameof(damagePerStr));
        if (damagePerAgi < 0)
            throw new ArgumentException("DamagePerAgi cannot be negative", nameof(damagePerAgi));
        if (damagePerInt < 0)
            throw new ArgumentException("DamagePerInt cannot be negative", nameof(damagePerInt));
        if (spreadMin < 0)
            throw new ArgumentException("SpreadMin cannot be negative", nameof(spreadMin));
        if (spreadMax < 0)
            throw new ArgumentException("SpreadMax cannot be negative", nameof(spreadMax));
        if (spreadMin >= spreadMax)
            throw new ArgumentException("SpreadMin must be less than SpreadMax", nameof(spreadMin));

        BaseWeaponDamage = baseWeaponDamage;
        DamagePerStr = damagePerStr;
        DamagePerAgi = damagePerAgi;
        DamagePerInt = damagePerInt;
        SpreadMin = spreadMin;
        SpreadMax = spreadMax;
    }
}

public sealed record MfBalance
{
    public int MfPerAgi { get; init; }
    public int MfPerInt { get; init; }

    public MfBalance(int mfPerAgi, int mfPerInt)
    {
        if (mfPerAgi <= 0)
            throw new ArgumentException("MfPerAgi must be positive", nameof(mfPerAgi));
        if (mfPerInt <= 0)
            throw new ArgumentException("MfPerInt must be positive", nameof(mfPerInt));

        MfPerAgi = mfPerAgi;
        MfPerInt = mfPerInt;
    }
}

public sealed record ChanceBalance
{
    public decimal Base { get; init; }
    public decimal Min { get; init; }
    public decimal Max { get; init; }
    public decimal Scale { get; init; }
    public decimal KBase { get; init; }

    public ChanceBalance(decimal @base, decimal min, decimal max, decimal scale, decimal kBase)
    {
        if (@base < 0)
            throw new ArgumentException("Base cannot be negative", nameof(@base));
        if (min < 0)
            throw new ArgumentException("Min cannot be negative", nameof(min));
        if (max < 0)
            throw new ArgumentException("Max cannot be negative", nameof(max));
        if (min > max)
            throw new ArgumentException("Min must be less than or equal to Max", nameof(min));
        if (scale < 0)
            throw new ArgumentException("Scale cannot be negative", nameof(scale));
        if (kBase <= 0)
            throw new ArgumentException("KBase must be positive", nameof(kBase));

        Base = @base;
        Min = min;
        Max = max;
        Scale = scale;
        KBase = kBase;
    }
}

public enum CritEffectMode
{
    Multiplier,
    BypassBlock,
    Hybrid
}

public sealed record CritEffectBalance
{
    public CritEffectMode Mode { get; init; }
    public decimal Multiplier { get; init; }
    public decimal HybridBlockMultiplier { get; init; }

    public CritEffectBalance(CritEffectMode mode, decimal multiplier, decimal hybridBlockMultiplier)
    {
        if (multiplier <= 0)
        {
            throw new ArgumentException("Multiplier must be positive", nameof(multiplier));
        }

        if (hybridBlockMultiplier < 0 || hybridBlockMultiplier > 1)
        {
            throw new ArgumentException("HybridBlockMultiplier must be between 0 and 1", nameof(hybridBlockMultiplier));
        }

        Mode = mode;
        Multiplier = multiplier;
        HybridBlockMultiplier = hybridBlockMultiplier;
    }
}


