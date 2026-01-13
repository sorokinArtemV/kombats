using Kombats.Battle.Domain.Rules;

namespace Kombats.Battle.Application.Abstractions;

/// <summary>
/// Port interface for providing battle rulesets from configuration.
/// Application uses this to get rulesets without depending on Infrastructure.
/// </summary>
public interface IRulesetProvider
{
    /// <summary>
    /// Gets the current active ruleset (based on CurrentVersion in configuration).
    /// </summary>
    /// <param name="mode">Optional mode/queue type. Currently unused, reserved for future use.</param>
    /// <returns>The current ruleset (without seed - seed is generated per battle).</returns>
    RulesetWithoutSeed GetCurrentRuleset(string? mode = null);

    /// <summary>
    /// Gets a ruleset by specific version.
    /// </summary>
    /// <param name="version">Ruleset version number.</param>
    /// <returns>The ruleset (without seed - seed is generated per battle).</returns>
    /// <exception cref="ArgumentException">If version is not found in configuration.</exception>
    RulesetWithoutSeed GetRulesetByVersion(int version);
}

/// <summary>
/// Ruleset configuration without seed (seed is generated per battle).
/// </summary>
public record RulesetWithoutSeed(
    int Version,
    int TurnSeconds,
    int NoActionLimit,
    CombatBalance Balance);

