using Combats.Battle.Api.Contracts.SignalR;
using Combats.Battle.Application.Ports;
using Combats.Battle.Application.UseCases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace Combats.Battle.Api.Hubs;

/// <summary>
/// SignalR hub for battle operations.
/// Thin adapter that delegates to Application services.
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

    public async Task<BattleSnapshotDto> JoinBattle(Guid battleId)
    {
        var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                         ?? Context.User?.FindFirst("sub")?.Value;
        
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            _logger.LogWarning(
                "Unauthenticated or invalid user attempting to join battle {BattleId}",
                battleId);
            throw new HubException("User not authenticated");
        }

        _logger.LogInformation(
            "User {UserId} joining battle {BattleId}, ConnectionId: {ConnectionId}",
            userId, battleId, Context.ConnectionId);

        // Add connection to battle group
        await Groups.AddToGroupAsync(Context.ConnectionId, $"battle:{battleId}");

        // Get current battle state (snapshot) - authoritative source
        BattleStateView? state;
        try
        {
            state = await _stateStore.GetStateAsync(battleId);
            if (state == null)
            {
                _logger.LogWarning(
                    "Battle {BattleId} not found for user {UserId}",
                    battleId, userId);
                throw new HubException($"Battle {battleId} not found");
            }
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex,
                "Failed to load battle state for BattleId: {BattleId}, UserId: {UserId}",
                battleId, userId);
            throw new HubException($"Battle {battleId} state is corrupted");
        }

        // Verify user is a participant
        if (state.PlayerAId != userId && state.PlayerBId != userId)
        {
            _logger.LogWarning(
                "User {UserId} is not a participant in battle {BattleId}",
                userId, battleId);
            throw new HubException("User is not a participant in this battle");
        }

        // Determine ended reason if battle is ended
        string? endedReason = null;
        if (state.Phase == BattlePhaseView.Ended)
        {
            // For now, we infer DoubleForfeit if NoActionStreakBoth >= NoActionLimit
            // In production, this should be stored in BattleState or retrieved from Postgres
            if (state.NoActionStreakBoth >= state.Ruleset.NoActionLimit)
            {
                endedReason = "DoubleForfeit";
            }
            else
            {
                endedReason = "Unknown"; // Fallback if ended for other reasons
            }
        }

        // Return typed snapshot DTO with DeadlineUtc as ISO string
        return new BattleSnapshotDto
        {
            BattleId = state.BattleId,
            PlayerAId = state.PlayerAId,
            PlayerBId = state.PlayerBId,
            Ruleset = state.Ruleset,
            Phase = state.Phase.ToString(),
            TurnIndex = state.TurnIndex,
            DeadlineUtc = state.DeadlineUtc.ToUniversalTime().ToString("O"),
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
            _logger.LogWarning(
                "Unauthenticated or invalid user attempting to submit action for battle {BattleId}",
                battleId);
            throw new HubException("User not authenticated");
        }

        _logger.LogInformation(
            "User {UserId} submitting action for BattleId: {BattleId}, TurnIndex: {TurnIndex}, ConnectionId: {ConnectionId}",
            userId, battleId, turnIndex, Context.ConnectionId);

        try
        {
            // Delegate to Application service for orchestration
            await _turnAppService.SubmitActionAsync(battleId, userId, turnIndex, actionPayload);
        }
        catch (InvalidOperationException ex)
        {
            // Convert domain exceptions to HubException for SignalR clients
            throw new HubException(ex.Message);
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation(
            "Client disconnected: ConnectionId: {ConnectionId}, Exception: {Exception}",
            Context.ConnectionId, exception?.Message);
        await base.OnDisconnectedAsync(exception);
    }
}

