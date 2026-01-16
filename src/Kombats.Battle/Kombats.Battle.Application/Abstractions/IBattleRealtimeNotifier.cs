using Kombats.Battle.Domain.Results;
using Kombats.Battle.Domain.Rules;

namespace Kombats.Battle.Application.Abstractions;

/// <summary>
/// Port interface for realtime notifications to battle participants.
/// Application defines what it needs; Infrastructure provides SignalR implementation.
/// </summary>
public interface IBattleRealtimeNotifier
{
    public Task NotifyBattleReadyAsync(
        Guid battleId, 
        Guid playerAId, 
        Guid playerBId,
        CancellationToken cancellationToken = default);

    public Task NotifyTurnOpenedAsync(
        Guid battleId, int turnIndex, 
        DateTimeOffset deadlineUtc,
        CancellationToken cancellationToken = default);

    public Task NotifyTurnResolvedAsync(
        Guid battleId, int turnIndex, 
        string playerAAction, 
        string playerBAction,
        TurnResolutionLog? log = null, 
        CancellationToken cancellationToken = default);

    public Task NotifyPlayerDamagedAsync(
        Guid battleId, 
        Guid playerId, 
        int damage, 
        int remainingHp, 
        int turnIndex,
        CancellationToken cancellationToken = default);

    public Task NotifyBattleStateUpdatedAsync(
        Guid battleId,
        Guid playerAId,
        Guid playerBId,
        Ruleset ruleset,
        string phase,
        int turnIndex,
        DateTimeOffset deadlineUtc,
        int noActionStreakBoth,
        int lastResolvedTurnIndex,
        string? endedReason,
        int version,
        int? playerAHp,
        int? playerBHp,
        CancellationToken cancellationToken = default);

    Task NotifyBattleEndedAsync(
        Guid battleId, 
        string reason, 
        Guid? winnerPlayerId, 
        DateTimeOffset endedAt,
        CancellationToken cancellationToken = default);
}