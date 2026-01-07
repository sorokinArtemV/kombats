using Combats.Battle.Api.Contracts.Realtime;
using Combats.Battle.Application.Ports;
using Combats.Contracts.Battle;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Combats.Battle.Infrastructure.Realtime.SignalR;

/// <summary>
/// SignalR implementation of IBattleRealtimeNotifier.
/// Maps Application parameters to SignalR DTOs.
/// Uses IHubContext&lt;Hub&gt; base class to avoid dependency on Api.Hubs.BattleHub.
/// </summary>
public class SignalRBattleRealtimeNotifier : IBattleRealtimeNotifier
{
    private readonly IHubContext<Hub> _hubContext;
    private readonly ILogger<SignalRBattleRealtimeNotifier> _logger;

    public SignalRBattleRealtimeNotifier(
        IHubContext<Hub> hubContext,
        ILogger<SignalRBattleRealtimeNotifier> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task NotifyBattleReadyAsync(Guid battleId, Guid playerAId, Guid playerBId, CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients.Group($"battle:{battleId}").SendAsync("BattleReady", new BattleReady
        {
            BattleId = battleId,
            PlayerAId = playerAId,
            PlayerBId = playerBId
        }, cancellationToken);
    }

    public async Task NotifyTurnOpenedAsync(Guid battleId, int turnIndex, DateTime deadlineUtc, CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients.Group($"battle:{battleId}").SendAsync("TurnOpened", new TurnOpened
        {
            BattleId = battleId,
            TurnIndex = turnIndex,
            DeadlineUtc = deadlineUtc.ToUniversalTime().ToString("O")
        }, cancellationToken);
    }

    public async Task NotifyTurnResolvedAsync(Guid battleId, int turnIndex, string playerAAction, string playerBAction, CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients.Group($"battle:{battleId}").SendAsync("TurnResolved", new TurnResolved
        {
            BattleId = battleId,
            TurnIndex = turnIndex,
            PlayerAAction = playerAAction,
            PlayerBAction = playerBAction
        }, cancellationToken);
    }

    public async Task NotifyPlayerDamagedAsync(Guid battleId, Guid playerId, int damage, int remainingHp, int turnIndex, CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients.Group($"battle:{battleId}").SendAsync("PlayerDamaged", new
        {
            BattleId = battleId,
            PlayerId = playerId,
            Damage = damage,
            RemainingHp = remainingHp,
            TurnIndex = turnIndex
        }, cancellationToken);
    }

    public async Task NotifyBattleStateUpdatedAsync(
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
        CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients.Group($"battle:{battleId}").SendAsync("BattleStateUpdated", new BattleSnapshot
        {
            BattleId = battleId,
            PlayerAId = playerAId,
            PlayerBId = playerBId,
            Ruleset = ruleset,
            Phase = phase,
            TurnIndex = turnIndex,
            DeadlineUtc = deadlineUtc.ToUniversalTime().ToString("O"),
            NoActionStreakBoth = noActionStreakBoth,
            LastResolvedTurnIndex = lastResolvedTurnIndex,
            EndedReason = endedReason,
            Version = version,
            PlayerAHp = playerAHp,
            PlayerBHp = playerBHp
        }, cancellationToken);
    }

    public async Task NotifyBattleEndedAsync(Guid battleId, string reason, Guid? winnerPlayerId, DateTime endedAt, CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients.Group($"battle:{battleId}").SendAsync("BattleEnded", new Combats.Battle.Api.Contracts.Realtime.BattleEnded
        {
            BattleId = battleId,
            Reason = reason,
            WinnerPlayerId = winnerPlayerId,
            EndedAt = endedAt.ToUniversalTime().ToString("O")
        }, cancellationToken);
    }
}



