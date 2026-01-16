namespace Combats.Battle.Realtime.Contracts;

/// <summary>
/// Battle ruleset contract for realtime notifications.
/// Contains only the fields needed by UI clients.
/// Do NOT reuse Domain.Rules.Ruleset directly to maintain boundary independence.
/// </summary>
public record BattleRulesetRealtime
{
    public int TurnSeconds { get; init; }
    public int? NoActionLimit { get; init; }
}






