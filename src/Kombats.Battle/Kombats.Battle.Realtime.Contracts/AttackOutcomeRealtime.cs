namespace Combats.Battle.Realtime.Contracts;

/// <summary>
/// Realtime contract for attack outcome.
/// </summary>
public enum AttackOutcomeRealtime
{
    NoAction,
    Dodged,
    Blocked,
    Hit,
    CriticalHit,
    CriticalBypassBlock,
    CriticalHybridBlocked
}

