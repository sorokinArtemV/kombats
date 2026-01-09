using Combats.Battle.Application.Abstractions;


namespace Combats.Battle.Application.UseCases.Lifecycle;

/// <summary>
/// Normalizes and validates Ruleset instances.
/// Converts from Contracts.Ruleset to Domain.Ruleset.
/// Ensures all ruleset values are within acceptable bounds and applies defaults when needed.
/// This is the single source of truth for ruleset normalization and validation.
/// </summary>
public class RulesetNormalizer
{
    private readonly BattleRulesDefaults _defaults;
    private readonly ICombatBalanceProvider _balanceProvider;

    public RulesetNormalizer(
        BattleRulesDefaults defaults,
        ICombatBalanceProvider balanceProvider)
    {
        _defaults = defaults;
        _balanceProvider = balanceProvider;
    }

    /// <summary>
    /// Normalizes a Ruleset from Contracts, applying defaults for null/missing values and enforcing bounds.
    /// Returns a Domain Ruleset with all values validated and normalized.
    /// </summary>
    /// <param name="incoming">The incoming ruleset from Contracts (may be null or have invalid values)</param>
    /// <returns>A normalized Domain Ruleset with all values within bounds</returns>
    public Domain.Rules.Ruleset Normalize(Contracts.Battle.Ruleset? incoming)
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

        // Get CombatBalance from provider
        var balance = _balanceProvider.GetBalance();

        return new Domain.Rules.Ruleset(
            version: incoming.Version > 0 ? incoming.Version : 1,
            turnSeconds: turnSeconds,
            noActionLimit: noActionLimit,
            seed: incoming.Seed,
            hpPerStamina: hpPerStamina,
            damagePerStrength: damagePerStrength,
            balance: balance);
    }

    /// <summary>
    /// Creates a default Domain Ruleset with all default values.
    /// </summary>
    public Domain.Rules.Ruleset CreateDefault()
    {
        // Get CombatBalance from provider
        var balance = _balanceProvider.GetBalance();

        return new Domain.Rules.Ruleset(
            version: 1,
            turnSeconds: _defaults.DefaultTurnSeconds,
            noActionLimit: _defaults.DefaultNoActionLimit,
            seed: 0, // Should be set by caller if needed
            hpPerStamina: _defaults.DefaultHpPerStamina,
            damagePerStrength: _defaults.DefaultDamagePerStrength,
            balance: balance);
    }
}



