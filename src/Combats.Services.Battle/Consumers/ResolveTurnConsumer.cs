using MassTransit;
using Microsoft.Extensions.Logging;
using Combats.Contracts.Battle;
using Combats.Services.Battle.State;
using Combats.Services.Battle.Constants;
using Combats.Services.Battle.DTOs;
using Combats.Services.Battle.Hubs;
using Combats.Services.Battle.Domain;
using Microsoft.AspNetCore.SignalR;

namespace Combats.Services.Battle.Consumers;

public class ResolveTurnConsumer : IConsumer<ResolveTurn>
{
    private readonly IBattleStateStore _stateStore;
    private readonly IBattleEngine _battleEngine;
    private readonly IHubContext<BattleHub> _hubContext;
    private readonly IMessageScheduler _messageScheduler;
    private readonly ILogger<ResolveTurnConsumer> _logger;

    public ResolveTurnConsumer(
        IBattleStateStore stateStore,
        IBattleEngine battleEngine,
        IHubContext<BattleHub> hubContext,
        IMessageScheduler messageScheduler,
        ILogger<ResolveTurnConsumer> logger)
    {
        _stateStore = stateStore;
        _battleEngine = battleEngine;
        _hubContext = hubContext;
        _messageScheduler = messageScheduler;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ResolveTurn> context)
    {
        var message = context.Message;
        var battleId = message.BattleId;
        var turnIndex = message.TurnIndex;

        _logger.LogInformation(
            "Processing ResolveTurn command for BattleId: {BattleId}, TurnIndex: {TurnIndex}, MessageId: {MessageId}, CorrelationId: {CorrelationId}",
            battleId, turnIndex, context.MessageId, context.CorrelationId);

        // Load state
        BattleState? state;
        try
        {
            state = await _stateStore.GetStateAsync(battleId, context.CancellationToken);
            if (state == null)
            {
                _logger.LogWarning(
                    "Battle state not found for BattleId: {BattleId}, TurnIndex: {TurnIndex}",
                    battleId, turnIndex);
                return;
            }
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex,
                "Failed to load battle state for BattleId: {BattleId}, TurnIndex: {TurnIndex}. State may be corrupted.",
                battleId, turnIndex);
            // ACK to avoid infinite retries on corrupted state
            return;
        }

        // Idempotency check: if turn already resolved, ACK and return
        if (turnIndex <= state.LastResolvedTurnIndex)
        {
            _logger.LogInformation(
                "Turn {TurnIndex} already resolved (LastResolvedTurnIndex: {LastResolvedTurnIndex}) for BattleId: {BattleId}. ACKing duplicate.",
                turnIndex, state.LastResolvedTurnIndex, battleId);
            return;
        }

        // Validate invariant: state must be TurnOpen and turnIndex must match
        if (state.Phase != State.BattlePhase.TurnOpen || state.TurnIndex != turnIndex)
        {
            // If battle is ended, just ACK
            if (state.Phase == State.BattlePhase.Ended)
            {
                _logger.LogInformation(
                    "Battle {BattleId} already ended, ignoring ResolveTurn for TurnIndex: {TurnIndex}",
                    battleId, turnIndex);
                return;
            }

            // If it's a duplicate that's already being resolved, ACK
            if (state.Phase == State.BattlePhase.Resolving && state.TurnIndex == turnIndex)
            {
                _logger.LogInformation(
                    "Turn {TurnIndex} already being resolved for BattleId: {BattleId}. ACKing duplicate.",
                    turnIndex, battleId);
                return;
            }

            // Invalid state that is not retryable - ACK and log error (do not infinite retry)
            _logger.LogError(
                "Invalid state for ResolveTurn: BattleId: {BattleId}, TurnIndex: {TurnIndex}, State.Phase: {Phase}, State.TurnIndex: {StateTurnIndex}. ACKing to avoid infinite retries.",
                battleId, turnIndex, state.Phase, state.TurnIndex);
            return;
        }

        // Log timing: check if scheduled message arrived earlier/later than deadline
        var deadlineUtc = state.GetDeadlineUtc();
        var now = DateTime.UtcNow;
        var timeDiff = (now - deadlineUtc).TotalSeconds;
        if (Math.Abs(timeDiff) > 1.0)
        {
            _logger.LogInformation(
                "ResolveTurn for BattleId: {BattleId}, TurnIndex: {TurnIndex} arrived {TimeDiffSeconds:F2}s {Direction} than deadline. Deadline: {DeadlineUtc}, Now: {NowUtc}",
                battleId, turnIndex, Math.Abs(timeDiff), timeDiff > 0 ? "later" : "earlier", deadlineUtc, now);
        }

        // Move to Resolving phase (atomic)
        var markedResolving = await _stateStore.TryMarkTurnResolvingAsync(battleId, turnIndex, context.CancellationToken);
        if (!markedResolving)
        {
            _logger.LogWarning(
                "Failed to mark turn {TurnIndex} as Resolving for BattleId: {BattleId}. May be duplicate or invalid state.",
                turnIndex, battleId);
            return;
        }

        // Reload state to get latest version
        BattleState? reloadedState;
        try
        {
            reloadedState = await _stateStore.GetStateAsync(battleId, context.CancellationToken);
            if (reloadedState == null)
            {
                _logger.LogError("Battle state disappeared after marking as Resolving for BattleId: {BattleId}", battleId);
                return;
            }
            state = reloadedState;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Failed to reload battle state after marking as Resolving for BattleId: {BattleId}", battleId);
            return;
        }

        // Read both actions for this turn (as raw payloads)
        var (playerAActionPayload, playerBActionPayload) = await _stateStore.GetActionsAsync(
            battleId,
            turnIndex,
            state.PlayerAId,
            state.PlayerBId,
            context.CancellationToken);

        // Parse actions into domain PlayerAction objects
        var playerAAction = ActionParser.ParseAction(playerAActionPayload, state.PlayerAId, turnIndex);
        var playerBAction = ActionParser.ParseAction(playerBActionPayload, state.PlayerBId, turnIndex);

        // Convert infrastructure state to domain state
        var domainState = BattleStateMapper.ToDomainState(state);

        // Resolve turn using battle engine (pure domain logic)
        var resolutionResult = _battleEngine.ResolveTurn(domainState, playerAAction, playerBAction);

        // Convert domain state back to infrastructure state
        var newInfrastructureState = BattleStateMapper.ToInfrastructureState(resolutionResult.NewState, state);
        
        // Preserve deadline and scheduling info
        newInfrastructureState.SetDeadlineUtc(state.GetDeadlineUtc());
        if (state.GetNextResolveScheduledUtc().HasValue)
        {
            newInfrastructureState.SetNextResolveScheduledUtc(state.GetNextResolveScheduledUtc().Value);
        }

        // Process domain events
        foreach (var domainEvent in resolutionResult.Events)
        {
            switch (domainEvent)
            {
                case BattleEndedDomainEvent battleEnded:
                    // Update Redis state to Ended (atomic)
                    var ended = await _stateStore.EndBattleAndMarkResolvedAsync(
                        battleId,
                        turnIndex,
                        resolutionResult.NewState.NoActionStreakBoth,
                        context.CancellationToken);
                    
                    if (ended)
                    {
                        // Publish BattleEnded contract event
                        var battleEndedContract = new BattleEnded
                        {
                            BattleId = battleId,
                            MatchId = state.MatchId,
                            Reason = battleEnded.Reason, // Same enum, no mapping needed
                            WinnerPlayerId = battleEnded.WinnerPlayerId,
                            EndedAt = battleEnded.OccurredAt,
                            Version = 1
                        };

                        await context.Publish(battleEndedContract, context.CancellationToken);

                        // Notify clients via SignalR
                        await _hubContext.Clients.Group($"battle:{battleId}").SendAsync("BattleEnded", new BattleEndedDto
                        {
                            BattleId = battleId,
                            Reason = battleEnded.Reason.ToString(),
                            WinnerPlayerId = battleEnded.WinnerPlayerId,
                            EndedAt = battleEnded.OccurredAt.ToUniversalTime().ToString("O")
                        }, context.CancellationToken);

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
                    return; // Battle ended, no need to continue

                case TurnResolvedDomainEvent turnResolved:
                    // Battle continues - open next turn
                    var nextTurnIndex = turnIndex + 1;
                    var turnSeconds = state.Ruleset.TurnSeconds;
                    var nextDeadline = DateTime.UtcNow.AddSeconds(turnSeconds);

                    // Update state with new HP and streak
                    var updatedState = BattleStateMapper.ToInfrastructureState(resolutionResult.NewState, state);
                    updatedState.SetDeadlineUtc(nextDeadline);
                    
                    // Save updated state (with HP and new streak)
                    // Note: We need to update state atomically. For now, we use MarkTurnResolvedAndOpenNextAsync
                    // which updates phase, turnIndex, and streak. HP needs to be saved separately or we extend the method.
                    // For MVP, we'll save state after opening next turn.
                    var nextTurnOpened = await _stateStore.MarkTurnResolvedAndOpenNextAsync(
                        battleId,
                        turnIndex,
                        nextTurnIndex,
                        nextDeadline,
                        resolutionResult.NewState.NoActionStreakBoth,
                        context.CancellationToken);

                    if (!nextTurnOpened)
                    {
                        _logger.LogError(
                            "Failed to open next turn {NextTurnIndex} for BattleId: {BattleId}",
                            nextTurnIndex, battleId);
                        return;
                    }

                    // Update HP in state
                    await _stateStore.UpdatePlayerHpAsync(
                        battleId,
                        resolutionResult.NewState.PlayerA.CurrentHp,
                        resolutionResult.NewState.PlayerB.CurrentHp,
                        context.CancellationToken);

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
                        }, context.CancellationToken);
                    }

                    // Send TurnResolved event with action details
                    var turnResolvedEvent = resolutionResult.Events.OfType<TurnResolvedDomainEvent>().FirstOrDefault();
                    if (turnResolvedEvent != null)
                    {
                        await _hubContext.Clients.Group($"battle:{battleId}").SendAsync("TurnResolved", new TurnResolvedDto
                        {
                            BattleId = battleId,
                            TurnIndex = turnIndex,
                            PlayerAAction = FormatAction(turnResolvedEvent.PlayerAAction),
                            PlayerBAction = FormatAction(turnResolvedEvent.PlayerBAction)
                        }, context.CancellationToken);
                    }

                    // Reload state to get authoritative deadline
                    var stateAfterTurnOpen = await _stateStore.GetStateAsync(battleId, context.CancellationToken);
                    if (stateAfterTurnOpen == null)
                    {
                        _logger.LogError("Battle state disappeared after opening next turn for BattleId: {BattleId}", battleId);
                        return;
                    }

                    var authoritativeNextDeadline = stateAfterTurnOpen.GetDeadlineUtc();

                    // Notify clients about next turn
                    await _hubContext.Clients.Group($"battle:{battleId}").SendAsync("TurnOpened", new TurnOpenedDto
                    {
                        BattleId = battleId,
                        TurnIndex = nextTurnIndex,
                        DeadlineUtc = authoritativeNextDeadline.ToUniversalTime().ToString("O")
                    }, context.CancellationToken);

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
                    }, context.CancellationToken);

                    // Schedule ResolveTurn for next turn
                    var nextResolveTurnCommand = new ResolveTurn(battleId, nextTurnIndex);

                    try
                    {
                        await _messageScheduler.ScheduleSend(
                            new Uri($"queue:{BattleQueues.ResolveTurn}"),
                            authoritativeNextDeadline,
                            nextResolveTurnCommand,
                            context.CancellationToken);

                        await _stateStore.MarkResolveScheduledAsync(battleId, authoritativeNextDeadline, context.CancellationToken);

                        _logger.LogInformation(
                            "Turn {TurnIndex} resolved and Turn {NextTurnIndex} opened for BattleId: {BattleId}. Next ResolveTurn scheduled for {DeadlineUtc}",
                            turnIndex, nextTurnIndex, battleId, authoritativeNextDeadline);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "Failed to schedule ResolveTurn for BattleId: {BattleId}, TurnIndex: {NextTurnIndex}. Watchdog will recover.",
                            battleId, nextTurnIndex);
                    }
                    break;

                case PlayerDamagedDomainEvent damageEvent:
                    // Already handled in TurnResolved case above
                    break;
            }
        }
    }

    // No mapping needed - domain uses same enum as contracts

    private static string FormatAction(Domain.PlayerAction action)
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
