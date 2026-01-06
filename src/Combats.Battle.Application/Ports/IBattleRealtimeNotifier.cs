using Combats.Contracts.Battle;

namespace Combats.Battle.Application.Ports;

/// <summary>
/// Port interface for realtime notifications to battle participants.
/// Application defines what it needs; Infrastructure provides SignalR implementation.
/// </summary>
public interface IBattleRealtimeNotifier
{
    Task NotifyBattleReadyAsync(Guid battleId, Guid playerAId, Guid playerBId, CancellationToken cancellationToken = default);
    Task NotifyTurnOpenedAsync(Guid battleId, int turnIndex, DateTime deadlineUtc, CancellationToken cancellationToken = default);
    Task NotifyTurnResolvedAsync(Guid battleId, int turnIndex, string playerAAction, string playerBAction, CancellationToken cancellationToken = default);
    Task NotifyPlayerDamagedAsync(Guid battleId, Guid playerId, int damage, int remainingHp, int turnIndex, CancellationToken cancellationToken = default);
    Task NotifyBattleStateUpdatedAsync(
        Guid battleId,
        Guid playerAId,
        Guid playerBId,
        Ruleset ruleset,
        string phase,
        int turnIndex,
        DateTime deadlineUtc,
        int noActionStreakBoth,
        int lastResolvedTurnIndex,
        string? endedReason,
        int version,
        int? playerAHp,
        int? playerBHp,
        CancellationToken cancellationToken = default);
    Task NotifyBattleEndedAsync(Guid battleId, string reason, Guid? winnerPlayerId, DateTime endedAt, CancellationToken cancellationToken = default);
}


