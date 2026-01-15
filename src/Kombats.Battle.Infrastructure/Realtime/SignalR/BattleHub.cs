using Kombats.Battle.Application.Abstractions;
using Kombats.Battle.Application.UseCases.Turns;
using Kombats.Battle.Domain.Model;
using Combats.Battle.Realtime.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

namespace Kombats.Battle.Infrastructure.Realtime.SignalR;

/// <summary>
/// SignalR hub for battle operations.
/// Thin adapter that delegates to Application services.
/// Located in Infrastructure to allow Infrastructure to depend on it without referencing Api.
/// </summary>
[Authorize]
public class BattleHub : Hub
{
    private readonly IBattleStateStore _stateStore;
    private readonly BattleTurnAppService _turnAppService;
    private readonly ILogger<BattleHub> _logger;

    public BattleHub(
        IBattleStateStore stateStore,
        BattleTurnAppService turnAppService,
        ILogger<BattleHub> logger)
    {
        _stateStore = stateStore;
        _turnAppService = turnAppService;
        _logger = logger;
    }

    public async Task<BattleSnapshotRealtime> JoinBattle(Guid battleId)
    {
        var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                         ?? Context.User?.FindFirst("sub")?.Value;
        
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            _logger.LogWarning("Unauthenticated or invalid user attempting to join battle {BattleId}", battleId);
            throw new HubException("User not authenticated");
        }

        _logger.LogInformation("User {UserId} joining battle {BattleId}, ConnectionId: {ConnectionId}", userId, battleId, Context.ConnectionId);
        
        await Groups.AddToGroupAsync(Context.ConnectionId, $"battle:{battleId}");

        // Get current battle state (snapshot) - authoritative source
        var state = await _stateStore.GetStateAsync(battleId);
        if (state is null)
        {
            _logger.LogWarning("Battle {BattleId} not found for user {UserId}", battleId, userId);
            throw new HubException($"Battle {battleId} not found");
        }
        
        if (state.PlayerAId != userId && state.PlayerBId != userId)
        {
            _logger.LogWarning("User {UserId} is not a participant in battle {BattleId}", userId, battleId);
            throw new HubException("User is not a participant in this battle");
        }
        
        BattleEndReasonRealtime? endedReason = null;
        if (state.Phase == BattlePhase.Ended)
        {
            // For now, we infer DoubleForfeit if NoActionStreakBoth >= NoActionLimit
            // In production, this should be stored in BattleState or retrieved from Postgres
            endedReason = state.NoActionStreakBoth >= state.Ruleset.NoActionLimit 
                ? BattleEndReasonRealtime.DoubleForfeit 
                : BattleEndReasonRealtime.Unknown; // Fallback if ended for other reasons
        }

        // Map BattlePhase to BattlePhaseRealtime
        var phaseRealtime = state.Phase switch
        {
            BattlePhase.ArenaOpen => BattlePhaseRealtime.ArenaOpen,
            BattlePhase.TurnOpen => BattlePhaseRealtime.TurnOpen,
            BattlePhase.Resolving => BattlePhaseRealtime.Resolving,
            BattlePhase.Ended => BattlePhaseRealtime.Ended,
            _ => throw new InvalidOperationException($"Unknown BattlePhase: {state.Phase}")
        };
        
        return new BattleSnapshotRealtime
        {
            BattleId = state.BattleId,
            PlayerAId = state.PlayerAId,
            PlayerBId = state.PlayerBId,
            Ruleset = new BattleRulesetRealtime
            {
                TurnSeconds = state.Ruleset.TurnSeconds,
                NoActionLimit = state.Ruleset.NoActionLimit
            },
            Phase = phaseRealtime,
            TurnIndex = state.TurnIndex,
            DeadlineUtc = new DateTimeOffset(state.DeadlineUtc.ToUniversalTime(), TimeSpan.Zero),
            NoActionStreakBoth = state.NoActionStreakBoth,
            LastResolvedTurnIndex = state.LastResolvedTurnIndex,
            EndedReason = endedReason,
            Version = state.Version,
            PlayerAHp = state.PlayerAHp,
            PlayerBHp = state.PlayerBHp
        };
    }

    public async Task SubmitTurnAction(Guid battleId, int turnIndex, string actionPayload)
    {
        var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                         ?? Context.User?.FindFirst("sub")?.Value;
        
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            _logger.LogWarning("Unauthenticated or invalid user attempting to submit action for battle {BattleId}", battleId);
            throw new HubException("User not authenticated");
        }

        _logger.LogInformation("User {UserId} submitting action for BattleId: {BattleId}, TurnIndex: {TurnIndex}, ConnectionId: {ConnectionId}", userId, battleId, turnIndex, Context.ConnectionId);

        try
        {
            await _turnAppService.SubmitActionAsync(battleId, userId, turnIndex, actionPayload);
        }
        catch (InvalidOperationException ex)
        {
            throw new HubException(ex.Message);
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: ConnectionId: {ConnectionId}, Exception: {Exception}", Context.ConnectionId, exception?.Message);
        await base.OnDisconnectedAsync(exception);
    }
}

