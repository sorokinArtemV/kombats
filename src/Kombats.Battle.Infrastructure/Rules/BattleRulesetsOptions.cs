using Kombats.Battle.Infrastructure.Profiles;

namespace Kombats.Battle.Infrastructure.Rules;

/// <summary>
/// Configuration options for versioned battle rulesets.
/// All battle rules come from appsettings - Battle service is authoritative.
/// </summary>
public class BattleRulesetsOptions
{
    public const string SectionName = "Battle:Rulesets";

    /// <summary>
    /// Current active ruleset version to use for new battles.
    /// </summary>
    public int CurrentVersion { get; set; }

    /// <summary>
    /// Dictionary of versioned ruleset configurations.
    /// Key is version number (as string for JSON binding), value is ruleset config.
    /// </summary>
    public Dictionary<string, RulesetVersionOptions> Versions { get; set; } = new();
}

/// <summary>
/// Configuration for a specific ruleset version.
/// </summary>
public class RulesetVersionOptions
{
    /// <summary>
    /// Turn duration in seconds.
    /// </summary>
    public int TurnSeconds { get; set; }

    /// <summary>
    /// Maximum consecutive NoAction turns before DoubleForfeit.
    /// </summary>
    public int NoActionLimit { get; set; }

    /// <summary>
    /// Seed generation policy. Currently only "RandomPerBattle" is supported.
    /// Optional - defaults to "RandomPerBattle".
    /// </summary>
    public string? SeedPolicy { get; set; } = "RandomPerBattle";

    /// <summary>
    /// Combat balance configuration for this ruleset version.
    /// </summary>
    public CombatBalanceVersionOptions CombatBalance { get; set; } = null!;
}

/// <summary>
/// Combat balance configuration for a ruleset version.
/// Mirrors the Domain CombatBalance structure.
/// </summary>
public class CombatBalanceVersionOptions
{
    public HpBalanceOptions Hp { get; set; } = null!;
    public DamageBalanceOptions Damage { get; set; } = null!;
    public MfBalanceOptions Mf { get; set; } = null!;
    public ChanceBalanceOptions DodgeChance { get; set; } = null!;
    public ChanceBalanceOptions CritChance { get; set; } = null!;
    public CritEffectBalanceOptions CritEffect { get; set; } = null!;
}

