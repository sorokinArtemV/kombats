using Combats.Battle.Domain.Model;

namespace Combats.Battle.Domain.Rules;

/// <summary>
/// SINGLE SOURCE OF TRUTH for all combat math formulas.
/// ALL combat calculations MUST be done through methods in this class.
/// NO formulas or magic numbers should exist elsewhere in the codebase.
/// </summary>
public static class CombatMath
{
    /// <summary>
    /// Computes derived combat stats from base PlayerStats and CombatBalance.
    /// </summary>
    public static DerivedCombatStats ComputeDerived(PlayerStats stats, CombatBalance balance)
    {
        // HP calculation: BaseHp + Endurance * HpPerEnd
        var hpMax = balance.Hp.BaseHp + stats.Stamina * balance.Hp.HpPerEnd;

        // Base damage calculation
        var baseDamage = balance.Damage.BaseWeaponDamage
                        + stats.Strength * balance.Damage.DamagePerStr
                        + stats.Agility * balance.Damage.DamagePerAgi
                        + stats.Intuition * balance.Damage.DamagePerInt;

        // Damage spread
        var damageMin = (int)Math.Floor(baseDamage * balance.Damage.SpreadMin);
        var damageMax = (int)Math.Ceiling(baseDamage * balance.Damage.SpreadMax);

        // Modifier factors (mf)
        var mfDodge = stats.Agility * balance.Mf.MfPerAgi;
        var mfAntiDodge = stats.Agility * balance.Mf.MfPerAgi;
        var mfCrit = stats.Intuition * balance.Mf.MfPerInt;
        var mfAntiCrit = stats.Intuition * balance.Mf.MfPerInt;

        return new DerivedCombatStats(
            hpMax: hpMax,
            damageMin: damageMin,
            damageMax: damageMax,
            mfDodge: mfDodge,
            mfAntiDodge: mfAntiDodge,
            mfCrit: mfCrit,
            mfAntiCrit: mfAntiCrit);
    }

    /// <summary>
    /// Computes a chance value based on rating difference using the formula:
    /// diff = ratingA - ratingB
    /// absDiff = abs(diff)
    /// raw = base + scale * diff / (absDiff + kBase)
    /// return clamp(raw, min, max)
    /// </summary>
    public static decimal ComputeChance(
        int diff,
        decimal @base,
        decimal min,
        decimal max,
        decimal scale,
        decimal kBase)
    {
        var absDiff = Math.Abs(diff);
        var raw = @base + scale * diff / (absDiff + kBase);
        return Math.Clamp(raw, min, max);
    }

    /// <summary>
    /// Computes dodge chance for an attack.
    /// diff = defender.MfDodge - attacker.MfAntiDodge
    /// </summary>
    public static decimal ComputeDodgeChance(
        DerivedCombatStats attackerDerived,
        DerivedCombatStats defenderDerived,
        CombatBalance balance)
    {
        var diff = defenderDerived.MfDodge - attackerDerived.MfAntiDodge;
        return ComputeChance(
            diff,
            balance.DodgeChance.Base,
            balance.DodgeChance.Min,
            balance.DodgeChance.Max,
            balance.DodgeChance.Scale,
            balance.DodgeChance.KBase);
    }

    /// <summary>
    /// Computes crit chance for an attack.
    /// diff = attacker.MfCrit - defender.MfAntiCrit
    /// </summary>
    public static decimal ComputeCritChance(
        DerivedCombatStats attackerDerived,
        DerivedCombatStats defenderDerived,
        CombatBalance balance)
    {
        var diff = attackerDerived.MfCrit - defenderDerived.MfAntiCrit;
        return ComputeChance(
            diff,
            balance.CritChance.Base,
            balance.CritChance.Min,
            balance.CritChance.Max,
            balance.CritChance.Scale,
            balance.CritChance.KBase);
    }

    /// <summary>
    /// Rolls a random damage value within the attacker's damage range.
    /// </summary>
    public static int RollDamage(IRandomProvider rng, DerivedCombatStats attacker)
    {
        var damage = rng.NextDecimal(attacker.DamageMin, attacker.DamageMax);
        return (int)Math.Round(damage, MidpointRounding.AwayFromZero);
    }
}

