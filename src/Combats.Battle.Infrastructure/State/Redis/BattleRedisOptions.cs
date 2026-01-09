namespace Combats.Battle.Infrastructure.State.Redis;

/// <summary>
/// Configuration options for Redis battle state store.
/// </summary>
public class BattleRedisOptions
{
    public const string SectionName = "Battle:Redis";

    /// <summary>
    /// Time-to-live for stored player actions.
    /// Default: 12 hours.
    /// </summary>
    public TimeSpan ActionTtl { get; set; } = TimeSpan.FromHours(12);

    /// <summary>
    /// Time-to-live for battle state after battle ends (optional, for cleanup).
    /// If null, state is kept indefinitely.
    /// </summary>
    public TimeSpan? StateTtlAfterEnd { get; set; } = null;
}


