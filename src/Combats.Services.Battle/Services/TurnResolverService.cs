using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Combats.Contracts.Battle;
using Combats.Services.Battle.Domain;
using Combats.Services.Battle.DTOs;
using Combats.Services.Battle.Hubs;
using Combats.Services.Battle.State;

namespace Combats.Services.Battle.Services;

/// <summary>
/// Service for resolving battle turns. Handles turn resolution logic that was previously in ResolveTurnConsumer.
/// Used by both TurnDeadlineWorker (deadline-driven) and BattleHub (early resolution).
/// </summary>
public class TurnResolverService
{
    private readonly IBattleStateStore _stateStore;
    private readonly IBattleEngine _battleEngine;
    private readonly IHubContext<BattleHub> _hubContext;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<TurnResolverService> _logger;

    public TurnResolverService(
        IBattleStateStore stateStore,
        IBattleEngine battleEngine,
        IHubContext<BattleHub> hubContext,
        IPublishEndpoint publishEndpoint,
        ILogger<TurnResolverService> logger)
    {
        _stateStore = stateStore;
        _battleEngine = battleEngine;
        _hubContext = hubContext;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    /// <summary>
    /// Resolves a turn for a battle. This method is idempotent and safe to call multiple times.
    /// </summary>
    /// <param name="battleId">The battle ID</param>
    /// <param name="turnIndex">The turn index to resolve</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the turn was resolved, false if it was already resolved or in invalid state</returns>
    public async Task<bool> ResolveTurnAsync(Guid battleId, int turnIndex, CancellationToken cancellationToken = default)
    {
        // Load state
        BattleState? state;
        try
        {
            state = await _stateStore.GetStateAsync(battleId, cancellationToken);
            if (state == null)
            {
                _logger.LogWarning(
                    "Battle state not found for BattleId: {BattleId}, TurnIndex: {TurnIndex}",
                    battleId, turnIndex);
                return false;
            }
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex,
                "Failed to load battle state for BattleId: {BattleId}, TurnIndex: {TurnIndex}. State may be corrupted.",
                battleId, turnIndex);
            return false;
        }

        // Idempotency check: if turn already resolved, return
        if (turnIndex <= state.LastResolvedTurnIndex)
        {
            _logger.LogInformation(
                "Turn {TurnIndex} already resolved (LastResolvedTurnIndex: {LastResolvedTurnIndex}) for BattleId: {BattleId}.",
                turnIndex, state.LastResolvedTurnIndex, battleId);
            return false;
        }

        // Validate invariant: state must be TurnOpen and turnIndex must match
        if (state.Phase != BattlePhase.TurnOpen || state.TurnIndex != turnIndex)
        {
            // If battle is ended, just return
            if (state.Phase == BattlePhase.Ended)
            {
                _logger.LogInformation(
                    "Battle {BattleId} already ended, ignoring ResolveTurn for TurnIndex: {TurnIndex}",
                    battleId, turnIndex);
                return false;
            }

            // If it's a duplicate that's already being resolved, return
            if (state.Phase == BattlePhase.Resolving && state.TurnIndex == turnIndex)
            {
                _logger.LogInformation(
                    "Turn {TurnIndex} already being resolved for BattleId: {BattleId}.",
                    turnIndex, battleId);
                return false;
            }

            // Invalid state
            _logger.LogError(
                "Invalid state for ResolveTurn: BattleId: {BattleId}, TurnIndex: {TurnIndex}, State.Phase: {Phase}, State.TurnIndex: {StateTurnIndex}.",
                battleId, turnIndex, state.Phase, state.TurnIndex);
            return false;
        }

        // Move to Resolving phase (atomic CAS)
        var markedResolving = await _stateStore.TryMarkTurnResolvingAsync(battleId, turnIndex, cancellationToken);
        if (!markedResolving)
        {
            _logger.LogWarning(
                "Failed to mark turn {TurnIndex} as Resolving for BattleId: {BattleId}. May be duplicate or invalid state.",
                turnIndex, battleId);
            return false;
        }

        // Reload state to get latest version
        BattleState? reloadedState;
        try
        {
            reloadedState = await _stateStore.GetStateAsync(battleId, cancellationToken);
            if (reloadedState == null)
            {
                _logger.LogError("Battle state disappeared after marking as Resolving for BattleId: {BattleId}", battleId);
                return false;
            }
            state = reloadedState;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Failed to reload battle state after marking as Resolving for BattleId: {BattleId}", battleId);
            return false;
        }

        // Read both actions for this turn (as raw payloads)
        var (playerAActionPayload, playerBActionPayload) = await _stateStore.GetActionsAsync(
            battleId,
            turnIndex,
            state.PlayerAId,
            state.PlayerBId,
            cancellationToken);

        // Parse actions into domain PlayerAction objects
        var playerAAction = ActionParser.ParseAction(playerAActionPayload, state.PlayerAId, turnIndex);
        var playerBAction = ActionParser.ParseAction(playerBActionPayload, state.PlayerBId, turnIndex);

        // Convert infrastructure state to domain state
        var domainState = BattleStateMapper.ToDomainState(state);

        // Resolve turn using battle engine (pure domain logic)
        var resolutionResult = _battleEngine.ResolveTurn(domainState, playerAAction, playerBAction);

        // Process domain events
        foreach (var domainEvent in resolutionResult.Events)
        {
            switch (domainEvent)
            {
                case BattleEndedDomainEvent battleEnded:
                    // Update Redis state to Ended (atomic) and remove from deadlines
                    var ended = await _stateStore.EndBattleAndMarkResolvedAsync(
                        battleId,
                        turnIndex,
                        resolutionResult.NewState.NoActionStreakBoth,
                        cancellationToken);
                    
                    if (ended)
                    {
                        // Publish BattleEnded contract event (via outbox)
                        var battleEndedContract = new BattleEnded
                        {
                            BattleId = battleId,
                            MatchId = state.MatchId,
                            Reason = battleEnded.Reason,
                            WinnerPlayerId = battleEnded.WinnerPlayerId,
                            EndedAt = battleEnded.OccurredAt,
                            Version = 1
                        };

                        await _publishEndpoint.Publish(battleEndedContract, cancellationToken);

                        // Notify clients via SignalR
                        await _hubContext.Clients.Group($"battle:{battleId}").SendAsync("BattleEnded", new BattleEndedDto
                        {
                            BattleId = battleId,
                            Reason = battleEnded.Reason.ToString(),
                            WinnerPlayerId = battleEnded.WinnerPlayerId,
                            EndedAt = battleEnded.OccurredAt.ToUniversalTime().ToString("O")
                        }, cancellationToken);

                        _logger.LogInformation(
                            "Battle {BattleId} ended. Reason: {Reason}, Winner: {WinnerPlayerId}",
                            battleId, battleEnded.Reason, battleEnded.WinnerPlayerId);
                    }
                    else
                    {
                        _logger.LogInformation(
                            "Battle {BattleId} already ended (duplicate ResolveTurn), skipping BattleEnded publish",
                            battleId);
                    }
                    return true; // Battle ended

                case TurnResolvedDomainEvent turnResolved:
                    // Battle continues - open next turn
                    var nextTurnIndex = turnIndex + 1;
                    var turnSeconds = state.Ruleset.TurnSeconds;
                    var nextDeadline = DateTime.UtcNow.AddSeconds(turnSeconds);

                    // Update state with new HP and streak, and open next turn (this also updates deadlines ZSET)
                    var nextTurnOpened = await _stateStore.MarkTurnResolvedAndOpenNextAsync(
                        battleId,
                        turnIndex,
                        nextTurnIndex,
                        nextDeadline,
                        resolutionResult.NewState.NoActionStreakBoth,
                        cancellationToken);

                    if (!nextTurnOpened)
                    {
                        _logger.LogError(
                            "Failed to open next turn {NextTurnIndex} for BattleId: {BattleId}",
                            nextTurnIndex, battleId);
                        return false;
                    }

                    // Update HP in state
                    await _stateStore.UpdatePlayerHpAsync(
                        battleId,
                        resolutionResult.NewState.PlayerA.CurrentHp,
                        resolutionResult.NewState.PlayerB.CurrentHp,
                        cancellationToken);

                    // Notify clients about turn resolution and damage
                    var damageEvents = resolutionResult.Events.OfType<PlayerDamagedDomainEvent>().ToList();
                    foreach (var damageEvent in damageEvents)
                    {
                        await _hubContext.Clients.Group($"battle:{battleId}").SendAsync("PlayerDamaged", new
                        {
                            BattleId = battleId,
                            PlayerId = damageEvent.PlayerId,
                            Damage = damageEvent.Damage,
                            RemainingHp = damageEvent.RemainingHp,
                            TurnIndex = damageEvent.TurnIndex
                        }, cancellationToken);
                    }

                    // Send TurnResolved event with action details
                    if (turnResolved != null)
                    {
                        await _hubContext.Clients.Group($"battle:{battleId}").SendAsync("TurnResolved", new TurnResolvedDto
                        {
                            BattleId = battleId,
                            TurnIndex = turnIndex,
                            PlayerAAction = FormatAction(turnResolved.PlayerAAction),
                            PlayerBAction = FormatAction(turnResolved.PlayerBAction)
                        }, cancellationToken);
                    }

                    // Reload state to get authoritative deadline
                    var stateAfterTurnOpen = await _stateStore.GetStateAsync(battleId, cancellationToken);
                    if (stateAfterTurnOpen == null)
                    {
                        _logger.LogError("Battle state disappeared after opening next turn for BattleId: {BattleId}", battleId);
                        return false;
                    }

                    var authoritativeNextDeadline = stateAfterTurnOpen.GetDeadlineUtc();

                    // Notify clients about next turn
                    await _hubContext.Clients.Group($"battle:{battleId}").SendAsync("TurnOpened", new TurnOpenedDto
                    {
                        BattleId = battleId,
                        TurnIndex = nextTurnIndex,
                        DeadlineUtc = authoritativeNextDeadline.ToUniversalTime().ToString("O")
                    }, cancellationToken);

                    // Send updated battle state with HP
                    await _hubContext.Clients.Group($"battle:{battleId}").SendAsync("BattleStateUpdated", new BattleSnapshotDto
                    {
                        BattleId = battleId,
                        PlayerAId = stateAfterTurnOpen.PlayerAId,
                        PlayerBId = stateAfterTurnOpen.PlayerBId,
                        Ruleset = stateAfterTurnOpen.Ruleset,
                        Phase = stateAfterTurnOpen.Phase.ToString(),
                        TurnIndex = nextTurnIndex,
                        DeadlineUtc = authoritativeNextDeadline.ToUniversalTime().ToString("O"),
                        NoActionStreakBoth = resolutionResult.NewState.NoActionStreakBoth,
                        LastResolvedTurnIndex = turnIndex,
                        EndedReason = null,
                        Version = stateAfterTurnOpen.Version,
                        PlayerAHp = resolutionResult.NewState.PlayerA.CurrentHp,
                        PlayerBHp = resolutionResult.NewState.PlayerB.CurrentHp
                    }, cancellationToken);

                    _logger.LogInformation(
                        "Turn {TurnIndex} resolved and Turn {NextTurnIndex} opened for BattleId: {BattleId}. Next deadline: {DeadlineUtc}",
                        turnIndex, nextTurnIndex, battleId, authoritativeNextDeadline);
                    break;

                case PlayerDamagedDomainEvent damageEvent:
                    // Already handled in TurnResolved case above
                    break;
            }
        }

        return true;
    }

    private static string FormatAction(PlayerAction action)
    {
        if (action.IsNoAction)
            return "NoAction";

        var attackZone = action.AttackZone?.ToString() ?? "None";
        if (action.BlockZonePrimary != null && action.BlockZoneSecondary != null)
        {
            return $"Attack: {attackZone}, Block: {action.BlockZonePrimary}-{action.BlockZoneSecondary}";
        }
        return $"Attack: {attackZone}";
    }
}

