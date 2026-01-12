using Kombats.Battle.Domain.Rules;

namespace Kombats.Battle.Infrastructure.Profiles;

/// <summary>
/// Mapper from Infrastructure CombatBalanceOptions to Domain CombatBalance.
/// </summary>
public static class CombatBalanceMapper
{
    public static CombatBalance ToDomain(CombatBalanceOptions options)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        var critEffectMode = ParseCritEffectMode(options.CritEffect.Mode);

        return new CombatBalance(
            hp: new HpBalance(options.Hp.BaseHp, options.Hp.HpPerEnd),
            damage: new DamageBalance(
                options.Damage.BaseWeaponDamage,
                options.Damage.DamagePerStr,
                options.Damage.DamagePerAgi,
                options.Damage.DamagePerInt,
                options.Damage.SpreadMin,
                options.Damage.SpreadMax),
            mf: new MfBalance(options.Mf.MfPerAgi, options.Mf.MfPerInt),
            dodgeChance: new ChanceBalance(
                options.DodgeChance.Base,
                options.DodgeChance.Min,
                options.DodgeChance.Max,
                options.DodgeChance.Scale,
                options.DodgeChance.KBase),
            critChance: new ChanceBalance(
                options.CritChance.Base,
                options.CritChance.Min,
                options.CritChance.Max,
                options.CritChance.Scale,
                options.CritChance.KBase),
            critEffect: new CritEffectBalance(
                critEffectMode,
                options.CritEffect.Multiplier,
                options.CritEffect.HybridBlockMultiplier));
    }

    private static CritEffectMode ParseCritEffectMode(string mode)
    {
        return mode switch
        {
            "Multiplier" => CritEffectMode.Multiplier,
            "BypassBlock" => CritEffectMode.BypassBlock,
            "Hybrid" => CritEffectMode.Hybrid,
            _ => throw new ArgumentException($"Unknown CritEffectMode: {mode}", nameof(mode))
        };
    }
}


