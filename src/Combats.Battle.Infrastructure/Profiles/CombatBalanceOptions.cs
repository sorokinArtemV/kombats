namespace Combats.Battle.Infrastructure.Profiles;

/// <summary>
/// Infrastructure options class for CombatBalance configuration.
/// Mirrors the Domain CombatBalance structure for appsettings binding.
/// </summary>
public class CombatBalanceOptions
{
    public const string SectionName = "CombatBalance";

    public HpBalanceOptions Hp { get; set; } = null!;
    public DamageBalanceOptions Damage { get; set; } = null!;
    public MfBalanceOptions Mf { get; set; } = null!;
    public ChanceBalanceOptions DodgeChance { get; set; } = null!;
    public ChanceBalanceOptions CritChance { get; set; } = null!;
    public CritEffectBalanceOptions CritEffect { get; set; } = null!;
}

public class HpBalanceOptions
{
    public int BaseHp { get; set; }
    public int HpPerEnd { get; set; }
}

public class DamageBalanceOptions
{
    public int BaseWeaponDamage { get; set; }
    public decimal DamagePerStr { get; set; }
    public decimal DamagePerAgi { get; set; }
    public decimal DamagePerInt { get; set; }
    public decimal SpreadMin { get; set; }
    public decimal SpreadMax { get; set; }
}

public class MfBalanceOptions
{
    public int MfPerAgi { get; set; }
    public int MfPerInt { get; set; }
}

public class ChanceBalanceOptions
{
    public decimal Base { get; set; }
    public decimal Min { get; set; }
    public decimal Max { get; set; }
    public decimal Scale { get; set; }
    public decimal KBase { get; set; }
}

public class CritEffectBalanceOptions
{
    public string Mode { get; set; } = "BypassBlock";
    public decimal Multiplier { get; set; }
    public decimal HybridBlockMultiplier { get; set; }
}

