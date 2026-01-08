namespace Combats.Battle.Realtime.Contracts;

/// <summary>
/// Strongly-typed event names for SignalR realtime notifications.
/// Use these constants instead of raw string literals to avoid typos and enable refactoring.
/// </summary>
public static class RealtimeEventNames
{
    public const string BattleReady = "BattleReady";
    public const string TurnOpened = "TurnOpened";
    public const string TurnResolved = "TurnResolved";
    public const string PlayerDamaged = "PlayerDamaged";
    public const string BattleStateUpdated = "BattleStateUpdated";
    public const string BattleEnded = "BattleEnded";
}

