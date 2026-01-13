namespace Kombats.Battle.Domain.Results;

/// <summary>
/// Represents the outcome of an attack resolution.
/// </summary>
public enum AttackOutcome
{
    NoAction,
    Dodged,
    Blocked,
    Hit,
    CriticalHit,
    CriticalBypassBlock,
    CriticalHybridBlocked
}

