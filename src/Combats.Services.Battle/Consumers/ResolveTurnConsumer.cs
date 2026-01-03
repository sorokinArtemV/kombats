using MassTransit;
using Microsoft.Extensions.Logging;
using Combats.Contracts.Battle;
using Combats.Services.Battle.State;
using Combats.Services.Battle.Constants;
using Combats.Services.Battle.DTOs;
using Combats.Services.Battle.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Combats.Services.Battle.Consumers;

public class ResolveTurnConsumer : IConsumer<ResolveTurn>
{
    private readonly IBattleStateStore _stateStore;
    private readonly IHubContext<BattleHub> _hubContext;
    private readonly IMessageScheduler _messageScheduler;
    private readonly ILogger<ResolveTurnConsumer> _logger;

    public ResolveTurnConsumer(
        IBattleStateStore stateStore,
        IHubContext<BattleHub> hubContext,
        IMessageScheduler messageScheduler,
        ILogger<ResolveTurnConsumer> logger)
    {
        _stateStore = stateStore;
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
        if (state.Phase != BattlePhase.TurnOpen || state.TurnIndex != turnIndex)
        {
            // If battle is ended, just ACK
            if (state.Phase == BattlePhase.Ended)
            {
                _logger.LogInformation(
                    "Battle {BattleId} already ended, ignoring ResolveTurn for TurnIndex: {TurnIndex}",
                    battleId, turnIndex);
                return;
            }

            // If it's a duplicate that's already being resolved, ACK
            if (state.Phase == BattlePhase.Resolving && state.TurnIndex == turnIndex)
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

        // Read both actions for this turn
        var (playerAAction, playerBAction) = await _stateStore.GetActionsAsync(
            battleId,
            turnIndex,
            state.PlayerAId,
            state.PlayerBId,
            context.CancellationToken);

        // Determine if actions are NoAction (missing or invalid)
        var playerANoAction = string.IsNullOrWhiteSpace(playerAAction);
        var playerBNoAction = string.IsNullOrWhiteSpace(playerBAction);

        // Apply minimal deterministic resolution
        int newNoActionStreak;
        if (playerANoAction && playerBNoAction)
        {
            newNoActionStreak = state.NoActionStreakBoth + 1;
            _logger.LogInformation(
                "Both players submitted NoAction for BattleId: {BattleId}, TurnIndex: {TurnIndex}. Streak: {Streak}",
                battleId, turnIndex, newNoActionStreak);
        }
        else
        {
            newNoActionStreak = 0;
            _logger.LogInformation(
                "At least one player submitted action for BattleId: {BattleId}, TurnIndex: {TurnIndex}",
                battleId, turnIndex);
        }

        // Check if battle should end due to DoubleForfeit (using NoActionLimit from Ruleset)
        var noActionLimit = state.Ruleset.NoActionLimit;
        if (newNoActionStreak >= noActionLimit)
        {
            // Use atomic EndBattleAndMarkResolvedAsync to ensure idempotency
            var ended = await _stateStore.EndBattleAndMarkResolvedAsync(battleId, turnIndex, newNoActionStreak, context.CancellationToken);
            if (ended)
            {
                // Publish BattleEnded event using context.Publish with MatchId from state
                var battleEnded = new BattleEnded
                {
                    BattleId = battleId,
                    MatchId = state.MatchId,
                    Reason = BattleEndReason.DoubleForfeit,
                    WinnerPlayerId = null,
                    EndedAt = DateTime.UtcNow,
                    Version = 1
                };

                await context.Publish(battleEnded, context.CancellationToken);

                // Notify clients via SignalR with typed DTO
                await _hubContext.Clients.Group($"battle:{battleId}").SendAsync("BattleEnded", new BattleEndedDto
                {
                    BattleId = battleId,
                    Reason = "DoubleForfeit",
                    WinnerPlayerId = null,
                    EndedAt = DateTime.UtcNow.ToUniversalTime().ToString("O")
                }, context.CancellationToken);

                _logger.LogInformation(
                    "Battle {BattleId} ended due to DoubleForfeit (NoAction streak: {Streak}, Limit: {Limit})",
                    battleId, newNoActionStreak, noActionLimit);
            }
            else
            {
                // Already ended by another instance - idempotent, just log
                _logger.LogInformation(
                    "Battle {BattleId} already ended (duplicate ResolveTurn), skipping BattleEnded publish",
                    battleId);
            }
            return;
        }

        // Open next turn using TurnSeconds from Ruleset
        // Cadence: Drifting (now + turnSeconds) - simpler and more predictable than fixed cadence
        // Rationale: Fixed cadence (max(now, deadline).AddSeconds) can cause issues if resolution is delayed,
        // as it would extend the next turn deadline. Drifting cadence maintains consistent turn duration.
        var nextTurnIndex = turnIndex + 1;
        var turnSeconds = state.Ruleset.TurnSeconds;
        var nextDeadline = DateTime.UtcNow.AddSeconds(turnSeconds);

        var nextTurnOpened = await _stateStore.MarkTurnResolvedAndOpenNextAsync(
            battleId,
            turnIndex,
            nextTurnIndex,
            nextDeadline,
            newNoActionStreak,
            context.CancellationToken);

        if (!nextTurnOpened)
        {
            _logger.LogError(
                "Failed to open next turn {NextTurnIndex} for BattleId: {BattleId}",
                nextTurnIndex, battleId);
            return;
        }

        // Reload state to get authoritative deadline
        state = await _stateStore.GetStateAsync(battleId, context.CancellationToken);
        if (state == null)
        {
            _logger.LogError("Battle state disappeared after opening next turn for BattleId: {BattleId}", battleId);
            return;
        }

        var authoritativeNextDeadline = state.GetDeadlineUtc();

        // Notify clients via SignalR with typed DTO using authoritative deadline
        await _hubContext.Clients.Group($"battle:{battleId}").SendAsync("TurnOpened", new TurnOpenedDto
        {
            BattleId = battleId,
            TurnIndex = nextTurnIndex,
            DeadlineUtc = authoritativeNextDeadline.ToUniversalTime().ToString("O")
        }, context.CancellationToken);

        // Schedule ResolveTurn for next turn
        var nextResolveTurnCommand = new ResolveTurn(battleId, nextTurnIndex);

        try
        {
            // Schedule using message scheduler to dedicated queue
            // IMessageScheduler.ScheduleSend doesn't support pipe configuration.
            // We use the scheduler directly - CorrelationId and MessageId will be set via message initialization.
            // Note: MassTransit will auto-generate MessageId if not set, and CorrelationId can be set via message context.
            await _messageScheduler.ScheduleSend(
                new Uri($"queue:{BattleQueues.ResolveTurn}"),
                authoritativeNextDeadline,
                nextResolveTurnCommand,
                context.CancellationToken);
            
            // Note: MassTransit auto-generates MessageId. CorrelationId should be battleId for observability.
            // For explicit control, we'd need to use ISendEndpointProvider with custom message initialization,
            // but IMessageScheduler.ScheduleSend is the recommended approach for delayed messages.

            // Mark as scheduled in state (for watchdog recovery)
            await _stateStore.MarkResolveScheduledAsync(battleId, authoritativeNextDeadline, context.CancellationToken);

            _logger.LogInformation(
                "Turn {TurnIndex} resolved and Turn {NextTurnIndex} opened for BattleId: {BattleId}. Next ResolveTurn scheduled for {DeadlineUtc} to queue {Queue}, CorrelationId: {CorrelationId}",
                turnIndex, nextTurnIndex, battleId, authoritativeNextDeadline, BattleQueues.ResolveTurn, battleId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to schedule ResolveTurn for BattleId: {BattleId}, TurnIndex: {NextTurnIndex}. Watchdog will recover.",
                battleId, nextTurnIndex);
            // Don't throw - watchdog will recover missing schedules
        }
    }
}
