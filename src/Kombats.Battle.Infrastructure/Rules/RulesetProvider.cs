using Kombats.Battle.Application.Abstractions;
using Kombats.Battle.Domain.Rules;
using Kombats.Battle.Infrastructure.Profiles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kombats.Battle.Infrastructure.Rules;

/// <summary>
/// Infrastructure implementation of IRulesetProvider.
/// Reads rulesets from appsettings configuration.
/// </summary>
public class RulesetProvider : IRulesetProvider
{
    private readonly BattleRulesetsOptions _options;
    private readonly ILogger<RulesetProvider> _logger;

    public RulesetProvider(
        IOptions<BattleRulesetsOptions> options,
        ILogger<RulesetProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public RulesetWithoutSeed GetCurrentRuleset(string? mode = null)
    {
        if (_options.CurrentVersion <= 0)
        {
            throw new InvalidOperationException(
                $"Battle:Rulesets:CurrentVersion must be greater than 0. Current value: {_options.CurrentVersion}");
        }

        return GetRulesetByVersion(_options.CurrentVersion);
    }

    public RulesetWithoutSeed GetRulesetByVersion(int version)
    {
        if (version <= 0)
        {
            throw new ArgumentException("Version must be greater than 0", nameof(version));
        }

        var versionKey = version.ToString();
        if (!_options.Versions.TryGetValue(versionKey, out var versionConfig))
        {
            throw new ArgumentException(
                $"Ruleset version {version} not found in configuration. Available versions: {string.Join(", ", _options.Versions.Keys)}",
                nameof(version));
        }

        // Validate version config
        if (versionConfig.TurnSeconds <= 0)
        {
            throw new InvalidOperationException(
                $"Ruleset version {version} has invalid TurnSeconds: {versionConfig.TurnSeconds}. Must be greater than 0.");
        }

        if (versionConfig.NoActionLimit <= 0)
        {
            throw new InvalidOperationException(
                $"Ruleset version {version} has invalid NoActionLimit: {versionConfig.NoActionLimit}. Must be greater than 0.");
        }

        if (versionConfig.CombatBalance == null)
        {
            throw new InvalidOperationException(
                $"Ruleset version {version} has null CombatBalance configuration.");
        }

        // Map CombatBalanceVersionOptions to Domain CombatBalance
        var balance = MapCombatBalance(versionConfig.CombatBalance);

        return new RulesetWithoutSeed(
            Version: version,
            TurnSeconds: versionConfig.TurnSeconds,
            NoActionLimit: versionConfig.NoActionLimit,
            Balance: balance);
    }

    private static CombatBalance MapCombatBalance(CombatBalanceVersionOptions options)
    {
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


