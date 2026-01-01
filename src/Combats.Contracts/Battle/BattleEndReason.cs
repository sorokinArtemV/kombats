namespace Combats.Contracts.Battle;

public enum BattleEndReason
{
    Normal = 0,
    DoubleForfeit = 1,
    Timeout = 2,
    Cancelled = 3,
    AdminForced = 4,
    SystemError = 5
}
