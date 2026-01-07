using Combats.Contracts.Battle;

namespace Combats.Battle.Application.Rules;

/// <summary>
/// Normalizes and validates Ruleset instances.
/// Ensures all ruleset values are within acceptable bounds and applies defaults when needed.
/// This is the single source of truth for ruleset normalization and validation.
/// </summary>
public class RulesetNormalizer
{
    private readonly BattleRulesDefaults _defaults;

    public RulesetNormalizer(BattleRulesDefaults defaults)
    {
        _defaults = defaults;
    }

    /// <summary>
    /// Normalizes a Ruleset, applying defaults for null/missing values and enforcing bounds.
    /// Returns a non-null Ruleset with all values validated and normalized.
    /// </summary>
    /// <param name="incoming">The incoming ruleset (may be null or have invalid values)</param>
    /// <returns>A normalized, non-null Ruleset with all values within bounds</returns>
    public Ruleset Normalize(Ruleset? incoming)
    {
        if (incoming == null)
        {
            return CreateDefault();
        }

        // Normalize TurnSeconds: enforce bounds [MinTurnSeconds, MaxTurnSeconds]
        var turnSeconds = incoming.TurnSeconds;
        if (turnSeconds < _defaults.MinTurnSeconds)
            turnSeconds = _defaults.MinTurnSeconds;
        else if (turnSeconds > _defaults.MaxTurnSeconds)
            turnSeconds = _defaults.MaxTurnSeconds;

        // Normalize NoActionLimit: enforce bounds [MinNoActionLimit, MaxNoActionLimit]
        var noActionLimit = incoming.NoActionLimit;
        if (noActionLimit < _defaults.MinNoActionLimit)
            noActionLimit = _defaults.MinNoActionLimit;
        else if (noActionLimit > _defaults.MaxNoActionLimit)
            noActionLimit = _defaults.MaxNoActionLimit;

        // Normalize HpPerStamina: ensure >= MinHpPerStamina, use default if <= 0
        var hpPerStamina = incoming.HpPerStamina;
        if (hpPerStamina <= 0)
            hpPerStamina = _defaults.DefaultHpPerStamina;
        else if (hpPerStamina < _defaults.MinHpPerStamina)
            hpPerStamina = _defaults.MinHpPerStamina;

        // Normalize DamagePerStrength: ensure >= MinDamagePerStrength, use default if <= 0
        var damagePerStrength = incoming.DamagePerStrength;
        if (damagePerStrength <= 0)
            damagePerStrength = _defaults.DefaultDamagePerStrength;
        else if (damagePerStrength < _defaults.MinDamagePerStrength)
            damagePerStrength = _defaults.MinDamagePerStrength;

        return new Ruleset
        {
            Version = incoming.Version > 0 ? incoming.Version : 1,
            TurnSeconds = turnSeconds,
            NoActionLimit = noActionLimit,
            Seed = incoming.Seed,
            HpPerStamina = hpPerStamina,
            DamagePerStrength = damagePerStrength
        };
    }

    /// <summary>
    /// Creates a default Ruleset with all default values.
    /// </summary>
    public Ruleset CreateDefault()
    {
        return new Ruleset
        {
            Version = 1,
            TurnSeconds = _defaults.DefaultTurnSeconds,
            NoActionLimit = _defaults.DefaultNoActionLimit,
            Seed = 0, // Should be set by caller if needed
            HpPerStamina = _defaults.DefaultHpPerStamina,
            DamagePerStrength = _defaults.DefaultDamagePerStrength
        };
    }
}

