using Combats.Battle.Application.Abstractions;
using Combats.Battle.Domain.Rules;
using Combats.Battle.Realtime.Contracts;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Combats.Battle.Infrastructure.Realtime.SignalR;

/// <summary>
/// SignalR implementation of IBattleRealtimeNotifier.
/// Maps Application parameters to typed SignalR contracts.
/// Uses IHubContext&lt;BattleHub&gt; to reference the hub type directly.
/// </summary>
public class SignalRBattleRealtimeNotifier : IBattleRealtimeNotifier
{
    private readonly IHubContext<BattleHub> _hubContext;
    private readonly ILogger<SignalRBattleRealtimeNotifier> _logger;

    public SignalRBattleRealtimeNotifier(
        IHubContext<BattleHub> hubContext,
        ILogger<SignalRBattleRealtimeNotifier> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task NotifyBattleReadyAsync(Guid battleId, Guid playerAId, Guid playerBId, CancellationToken cancellationToken = default)
    {
        var payload = new BattleReadyRealtime
        {
            BattleId = battleId,
            PlayerAId = playerAId,
            PlayerBId = playerBId
        };

        await _hubContext.Clients.Group($"battle:{battleId}").SendAsync(
            RealtimeEventNames.BattleReady,
            payload,
            cancellationToken);
    }

    public async Task NotifyTurnOpenedAsync(Guid battleId, int turnIndex, DateTime deadlineUtc, CancellationToken cancellationToken = default)
    {
        var payload = new TurnOpenedRealtime
        {
            BattleId = battleId,
            TurnIndex = turnIndex,
            DeadlineUtc = new DateTimeOffset(deadlineUtc.ToUniversalTime(), TimeSpan.Zero)
        };

        await _hubContext.Clients.Group($"battle:{battleId}").SendAsync(
            RealtimeEventNames.TurnOpened,
            payload,
            cancellationToken);
    }

    public async Task NotifyTurnResolvedAsync(Guid battleId, int turnIndex, string playerAAction, string playerBAction, CancellationToken cancellationToken = default)
    {
        var payload = new TurnResolvedRealtime
        {
            BattleId = battleId,
            TurnIndex = turnIndex,
            PlayerAAction = playerAAction,
            PlayerBAction = playerBAction
        };

        await _hubContext.Clients.Group($"battle:{battleId}").SendAsync(
            RealtimeEventNames.TurnResolved,
            payload,
            cancellationToken);
    }

    public async Task NotifyPlayerDamagedAsync(Guid battleId, Guid playerId, int damage, int remainingHp, int turnIndex, CancellationToken cancellationToken = default)
    {
        var payload = new PlayerDamagedRealtime
        {
            BattleId = battleId,
            PlayerId = playerId,
            Damage = damage,
            RemainingHp = remainingHp,
            TurnIndex = turnIndex
        };

        await _hubContext.Clients.Group($"battle:{battleId}").SendAsync(
            RealtimeEventNames.PlayerDamaged,
            payload,
            cancellationToken);
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
        var payload = new BattleStateUpdatedRealtime
        {
            BattleId = battleId,
            PlayerAId = playerAId,
            PlayerBId = playerBId,
            Ruleset = RealtimeContractMapper.ToRealtimeRuleset(ruleset),
            Phase = RealtimeContractMapper.ToRealtimePhase(phase, _logger),
            TurnIndex = turnIndex,
            DeadlineUtc = new DateTimeOffset(deadlineUtc.ToUniversalTime(), TimeSpan.Zero),
            NoActionStreakBoth = noActionStreakBoth,
            LastResolvedTurnIndex = lastResolvedTurnIndex,
            EndedReason = RealtimeContractMapper.ToRealtimeEndReason(endedReason, _logger),
            Version = version,
            PlayerAHp = playerAHp,
            PlayerBHp = playerBHp
        };

        await _hubContext.Clients.Group($"battle:{battleId}").SendAsync(
            RealtimeEventNames.BattleStateUpdated,
            payload,
            cancellationToken);
    }

    public async Task NotifyBattleEndedAsync(Guid battleId, string reason, Guid? winnerPlayerId, DateTime endedAt, CancellationToken cancellationToken = default)
    {
        var endReason = RealtimeContractMapper.ToRealtimeEndReason(reason, _logger) 
                       ?? BattleEndReasonRealtime.Unknown;

        var payload = new BattleEndedRealtime
        {
            BattleId = battleId,
            Reason = endReason,
            WinnerPlayerId = winnerPlayerId,
            EndedAt = new DateTimeOffset(endedAt.ToUniversalTime(), TimeSpan.Zero)
        };

        await _hubContext.Clients.Group($"battle:{battleId}").SendAsync(
            RealtimeEventNames.BattleEnded,
            payload,
            cancellationToken);
    }
}



