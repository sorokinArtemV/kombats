namespace Combats.Battle.Realtime.Contracts;

/// <summary>
/// Battle end reason enum for realtime contracts.
/// Maps to Domain.Results.EndBattleReason but kept separate to maintain boundary independence.
/// </summary>
public enum BattleEndReasonRealtime
{
    Normal = 0,
    DoubleForfeit = 1,
    Timeout = 2,
    Cancelled = 3,
    AdminForced = 4,
    SystemError = 5,
    Unknown = 99 // For cases where reason cannot be determined
}






