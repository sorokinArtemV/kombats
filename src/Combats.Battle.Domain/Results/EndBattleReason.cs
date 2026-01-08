namespace Combats.Battle.Domain.Results;

/// <summary>
/// Reason why a battle ended.
/// This is a domain enum, independent of messaging contracts.
/// </summary>
public enum EndBattleReason
{
    Normal = 0,
    DoubleForfeit = 1,
    Timeout = 2,
    Cancelled = 3,
    AdminForced = 4,
    SystemError = 5
}

