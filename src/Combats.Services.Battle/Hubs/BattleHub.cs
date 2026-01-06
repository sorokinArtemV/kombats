using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Combats.Services.Battle.State;
using Combats.Services.Battle.DTOs;
using Combats.Services.Battle.Services;
using Combats.Contracts.Battle;

namespace Combats.Services.Battle.Hubs;

[Authorize]
public class BattleHub : Hub
{
    private readonly IBattleStateStore _stateStore;
    private readonly TurnResolverService _turnResolver;
    private readonly ILogger<BattleHub> _logger;

    public BattleHub(
        IBattleStateStore stateStore,
        TurnResolverService turnResolver,
        ILogger<BattleHub> logger)
    {
        _stateStore = stateStore;
        _turnResolver = turnResolver;
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
        BattleState? state;
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
        if (state.Phase == BattlePhase.Ended)
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

        // Return typed snapshot DTO with DeadlineUtc as ISO string from GetDeadlineUtc()
        return new BattleSnapshotDto
        {
            BattleId = state.BattleId,
            PlayerAId = state.PlayerAId,
            PlayerBId = state.PlayerBId,
            Ruleset = state.Ruleset,
            Phase = state.Phase.ToString(),
            TurnIndex = state.TurnIndex,
            DeadlineUtc = state.GetDeadlineUtc().ToUniversalTime().ToString("O"),
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

        // Get current battle state - authoritative source
        BattleState? state;
        try
        {
            state = await _stateStore.GetStateAsync(battleId);
            if (state == null)
            {
                _logger.LogWarning(
                    "Battle {BattleId} not found for action submission",
                    battleId);
                throw new HubException($"Battle {battleId} not found");
            }
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex,
                "Failed to load battle state for BattleId: {BattleId} during action submission",
                battleId);
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

        // If battle is ended, reject action submission
        if (state.Phase == BattlePhase.Ended)
        {
            _logger.LogWarning(
                "Battle {BattleId} is ended, rejecting action submission from UserId: {UserId}",
                battleId, userId);
            throw new HubException("Battle has ended");
        }

        // Get current server turnIndex and deadline using helper methods
        var currentServerTurnIndex = state.TurnIndex;
        var deadlineUtc = state.GetDeadlineUtc();

        // Validate state: must be TurnOpen
        if (state.Phase != BattlePhase.TurnOpen)
        {
            var reason = $"InvalidPhase:{state.Phase}";
            _logger.LogWarning(
                "Invalid phase for action submission: BattleId: {BattleId}, ClientTurnIndex: {ClientTurnIndex}, Phase: {Phase}, PlayerId: {PlayerId}, TurnIndex: {TurnIndex}, DeadlineUtc: {DeadlineUtc}, Reason: {Reason}",
                battleId, turnIndex, state.Phase, userId, currentServerTurnIndex, deadlineUtc, reason);
            // Store NoAction for the current server turnIndex (not the client-provided turnIndex)
            await _stateStore.StoreActionAsync(battleId, currentServerTurnIndex, userId, string.Empty);
            return;
        }

        // If turnIndex mismatch, store NoAction for current server turnIndex
        if (currentServerTurnIndex != turnIndex)
        {
            var reason = $"TurnIndexMismatch:Expected={currentServerTurnIndex},Received={turnIndex}";
            _logger.LogWarning(
                "TurnIndex mismatch for action submission: BattleId: {BattleId}, Expected: {ExpectedTurnIndex}, Received: {ReceivedTurnIndex}, PlayerId: {PlayerId}, DeadlineUtc: {DeadlineUtc}, Reason: {Reason}. Storing NoAction for server turnIndex.",
                battleId, currentServerTurnIndex, turnIndex, userId, deadlineUtc, reason);
            await _stateStore.StoreActionAsync(battleId, currentServerTurnIndex, userId, string.Empty);
            return;
        }

        // Validate deadline hasn't passed (with small buffer for network latency)
        if (DateTime.UtcNow > deadlineUtc.AddSeconds(1))
        {
            var reason = "DeadlinePassed";
            _logger.LogWarning(
                "Deadline passed for action submission: BattleId: {BattleId}, TurnIndex: {TurnIndex}, DeadlineUtc: {DeadlineUtc}, PlayerId: {PlayerId}, Reason: {Reason}",
                battleId, turnIndex, deadlineUtc, userId, reason);
            // Store NoAction for current server turnIndex
            await _stateStore.StoreActionAsync(battleId, currentServerTurnIndex, userId, string.Empty);
            return;
        }

        // Validate payload: parse JSON to ensure it's valid JSON
        string finalActionPayload;
        string? noActionReason = null;
        if (string.IsNullOrWhiteSpace(actionPayload))
        {
            noActionReason = "EmptyPayload";
            _logger.LogInformation(
                "Empty action payload for BattleId: {BattleId}, TurnIndex: {TurnIndex}, UserId: {UserId}, Reason: {Reason}. Storing as NoAction.",
                battleId, turnIndex, userId, noActionReason);
            finalActionPayload = string.Empty;
        }
        else
        {
            try
            {
                // Validate JSON by parsing it
                using var doc = JsonDocument.Parse(actionPayload);
                // JSON is valid, use as-is
                finalActionPayload = actionPayload;
            }
            catch (JsonException ex)
            {
                noActionReason = "InvalidJSON";
                _logger.LogWarning(
                    ex,
                    "Invalid JSON in action payload for BattleId: {BattleId}, TurnIndex: {TurnIndex}, UserId: {UserId}, DeadlineUtc: {DeadlineUtc}, Reason: {Reason}. Treating as NoAction.",
                    battleId, turnIndex, userId, deadlineUtc, noActionReason);
                finalActionPayload = string.Empty;
            }
        }

        // Store action for current server turnIndex
        await _stateStore.StoreActionAsync(battleId, currentServerTurnIndex, userId, finalActionPayload);

        if (noActionReason != null)
        {
            _logger.LogInformation(
                "NoAction stored for BattleId: {BattleId}, TurnIndex: {TurnIndex}, UserId: {UserId}, Reason: {Reason}",
                battleId, currentServerTurnIndex, userId, noActionReason);
        }
        else
        {
            _logger.LogInformation(
                "Action stored for BattleId: {BattleId}, TurnIndex: {TurnIndex}, UserId: {UserId}",
                battleId, currentServerTurnIndex, userId);
        }

        // Early turn resolution: if both players have submitted actions, try to resolve immediately
        // This is a best-effort optimization - if CAS fails, the deadline worker will handle it
        try
        {
            // Check if both players have actions stored
            var (playerAAction, playerBAction) = await _stateStore.GetActionsAsync(
                battleId,
                currentServerTurnIndex,
                state.PlayerAId,
                state.PlayerBId);

            // Both actions are present (even if empty/NoAction)
            if (playerAAction != null && playerBAction != null)
            {
                // Try to resolve the turn immediately
                // TurnResolverService uses CAS (TryMarkTurnResolvingAsync) to ensure only one resolution happens
                var resolved = await _turnResolver.ResolveTurnAsync(battleId, currentServerTurnIndex);
                
                if (resolved)
                {
                    _logger.LogInformation(
                        "Early turn resolution successful for BattleId: {BattleId}, TurnIndex: {TurnIndex}",
                        battleId, currentServerTurnIndex);
                }
                // If resolved == false, either:
                // - Turn was already being resolved by deadline worker
                // - Turn was already resolved
                // - Invalid state
                // This is fine - no-op, deadline worker will handle it if needed
            }
        }
        catch (Exception ex)
        {
            // Don't fail the action submission if early resolution fails
            // Deadline worker will handle it
            _logger.LogWarning(ex,
                "Early turn resolution failed for BattleId: {BattleId}, TurnIndex: {TurnIndex}. Deadline worker will handle it.",
                battleId, currentServerTurnIndex);
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
