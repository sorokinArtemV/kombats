using Combats.Battle.Application.Mapping;
using Combats.Battle.Application.Abstractions;
using Combats.Battle.Domain.Engine;
using Combats.Battle.Domain.Events;
using Combats.Battle.Domain.Model;
using Combats.Battle.Domain.Rules;
using Microsoft.Extensions.Logging;

namespace Combats.Battle.Application.UseCases.Turns;

/// <summary>
/// Application service for battle turn operations: submitting actions and resolving turns.
/// Orchestrates turn resolution with proper idempotency and state machine enforcement.
/// </summary>
public class BattleTurnAppService
{
    private readonly IBattleStateStore _stateStore;
    private readonly IBattleEngine _battleEngine;
    private readonly IBattleRealtimeNotifier _notifier;
    private readonly IBattleEventPublisher _eventPublisher;
    private readonly PlayerActionNormalizer _actionNormalizer;
    private readonly IClock _clock;
    private readonly ILogger<BattleTurnAppService> _logger;

    public BattleTurnAppService(
        IBattleStateStore stateStore,
        IBattleEngine battleEngine,
        IBattleRealtimeNotifier notifier,
        IBattleEventPublisher eventPublisher,
        PlayerActionNormalizer actionNormalizer,
        IClock clock,
        ILogger<BattleTurnAppService> logger)
    {
        _stateStore = stateStore;
        _battleEngine = battleEngine;
        _notifier = notifier;
        _eventPublisher = eventPublisher;
        _actionNormalizer = actionNormalizer;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Submits a player action for the current turn.
    /// Validates protocol (phase, turn index, deadline) and normalizes payload.
    /// If both players have submitted actions, triggers early resolution (best-effort).
    /// </summary>
    public async Task SubmitActionAsync(
        Guid battleId,
        Guid playerId,
        int clientTurnIndex,
        string? actionPayload,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Submitting action for BattleId: {BattleId}, PlayerId: {PlayerId}, TurnIndex: {TurnIndex}",
            battleId, playerId, clientTurnIndex);

        // Load state
        var state = await _stateStore.GetStateAsync(battleId, cancellationToken);
        if (state == null)
        {
            _logger.LogWarning("Battle state not found for BattleId: {BattleId}", battleId);
            throw new InvalidOperationException($"Battle {battleId} not found");
        }

        // Verify player is a participant
        if (state.PlayerAId != playerId && state.PlayerBId != playerId)
        {
            _logger.LogWarning(
                "User {PlayerId} is not a participant in battle {BattleId}",
                playerId, battleId);
            throw new InvalidOperationException("User is not a participant in this battle");
        }

        // If battle is ended, reject
        if (state.Phase == BattlePhase.Ended)
        {
            _logger.LogWarning(
                "Battle {BattleId} is ended, rejecting action submission from PlayerId: {PlayerId}",
                battleId, playerId);
            throw new InvalidOperationException("Battle has ended");
        }

        // Normalize action payload (validates protocol and converts invalid to NoAction)
        var normalizedPayload = _actionNormalizer.NormalizeActionPayload(
            state,
            clientTurnIndex,
            actionPayload,
            playerId);

        // Store action for current server turn index (first-write-wins)
        var storeResult = await _stateStore.StoreActionAsync(battleId, state.TurnIndex, playerId, normalizedPayload, cancellationToken);

        if (storeResult == ActionStoreResult.Accepted)
        {
            _logger.LogInformation(
                "Action stored for BattleId: {BattleId}, TurnIndex: {TurnIndex}, PlayerId: {PlayerId}, IsNoAction: {IsNoAction}",
                battleId, state.TurnIndex, playerId, string.IsNullOrEmpty(normalizedPayload));
        }
        else
        {
            _logger.LogInformation(
                "Action already submitted for BattleId: {BattleId}, TurnIndex: {TurnIndex}, PlayerId: {PlayerId}. Skipping duplicate submission.",
                battleId, state.TurnIndex, playerId);
        }

        // Early resolution optimization: if both players have actions, try to resolve immediately
        // This is best-effort; if CAS fails, deadline worker will handle it
        try
        {
            var (playerAAction, playerBAction) = await _stateStore.GetActionsAsync(
                battleId,
                state.TurnIndex,
                state.PlayerAId,
                state.PlayerBId,
                cancellationToken);

            if (playerAAction != null && playerBAction != null)
            {
                // Both actions present - try early resolution
                // ResolveTurnAsync uses CAS, so it's safe to call even if another thread/worker is resolving
                await ResolveTurnAsync(battleId, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            // Don't fail action submission if early resolution fails
            _logger.LogWarning(ex,
                "Early turn resolution failed for BattleId: {BattleId}, TurnIndex: {TurnIndex}. Deadline worker will handle it.",
                battleId, state.TurnIndex);
        }
    }

    /// <summary>
    /// Resolves a turn for a battle.
    /// Idempotent: safe to call multiple times (CAS ensures only one resolution succeeds).
    /// 
    /// State machine enforcement:
    /// - Must be in TurnOpen phase
    /// - TurnIndex must match current state
    /// - Uses CAS (TryMarkTurnResolvingAsync) to ensure atomic transition
    /// 
    /// After successful commit:
    /// - Notifies clients via IBattleRealtimeNotifier
    /// - Publishes BattleEnded integration event (if battle ended)
    /// </summary>
    public async Task<bool> ResolveTurnAsync(Guid battleId, CancellationToken cancellationToken = default)
    {
        // Load state
        var state = await _stateStore.GetStateAsync(battleId, cancellationToken);
        if (state == null)
        {
            _logger.LogWarning("Battle state not found for BattleId: {BattleId}", battleId);
            return false;
        }

        var turnIndex = state.TurnIndex;

        // Idempotency check: if turn already resolved, return
        if (turnIndex <= state.LastResolvedTurnIndex)
        {
            _logger.LogInformation(
                "Turn {TurnIndex} already resolved (LastResolvedTurnIndex: {LastResolvedTurnIndex}) for BattleId: {BattleId}",
                turnIndex, state.LastResolvedTurnIndex, battleId);
            return false;
        }

        // State machine validation: must be TurnOpen and turnIndex must match
        if (state.Phase != BattlePhase.TurnOpen || state.TurnIndex != turnIndex)
        {
            if (state.Phase == BattlePhase.Ended)
            {
                _logger.LogInformation(
                    "Battle {BattleId} already ended, ignoring ResolveTurn for TurnIndex: {TurnIndex}",
                    battleId, turnIndex);
                return false;
            }

            if (state.Phase == BattlePhase.Resolving && state.TurnIndex == turnIndex)
            {
                _logger.LogInformation(
                    "Turn {TurnIndex} already being resolved for BattleId: {BattleId}",
                    turnIndex, battleId);
                return false;
            }

            _logger.LogError(
                "Invalid state for ResolveTurn: BattleId: {BattleId}, TurnIndex: {TurnIndex}, State.Phase: {Phase}, State.TurnIndex: {StateTurnIndex}",
                battleId, turnIndex, state.Phase, state.TurnIndex);
            return false;
        }

        // Atomic CAS: transition to Resolving phase
        var markedResolving = await _stateStore.TryMarkTurnResolvingAsync(battleId, turnIndex, cancellationToken);
        if (!markedResolving)
        {
            _logger.LogWarning(
                "Failed to mark turn {TurnIndex} as Resolving for BattleId: {BattleId}. May be duplicate or invalid state.",
                turnIndex, battleId);
            return false;
        }

        // Reload state to get latest version after CAS
        state = await _stateStore.GetStateAsync(battleId, cancellationToken);
        if (state == null)
        {
            _logger.LogError("Battle state disappeared after marking as Resolving for BattleId: {BattleId}", battleId);
            return false;
        }

        // Read actions for both players
        var (playerAActionPayload, playerBActionPayload) = await _stateStore.GetActionsAsync(
            battleId,
            turnIndex,
            state.PlayerAId,
            state.PlayerBId,
            cancellationToken);

        // Parse actions into domain objects
        var playerAAction = ActionParser.ParseAction(playerAActionPayload, state.PlayerAId, turnIndex);
        var playerBAction = ActionParser.ParseAction(playerBActionPayload, state.PlayerBId, turnIndex);

        // Convert to domain state
        var domainState = BattleStateToDomainMapper.ToDomainState(state);

        // Resolve turn using domain engine (pure logic)
        var resolutionResult = _battleEngine.ResolveTurn(domainState, playerAAction, playerBAction);

        // Process domain events and commit to Redis
        foreach (var domainEvent in resolutionResult.Events)
        {
            switch (domainEvent)
            {
                case BattleEndedDomainEvent battleEnded:
                    // Commit battle end atomically (includes HP update)
                    var endResult = await _stateStore.EndBattleAndMarkResolvedAsync(
                        battleId,
                        turnIndex,
                        resolutionResult.NewState.NoActionStreakBoth,
                        resolutionResult.NewState.PlayerA.CurrentHp,
                        resolutionResult.NewState.PlayerB.CurrentHp,
                        cancellationToken);

                    if (endResult == EndBattleCommitResult.EndedNow)
                    {
                        // Only notify/publish if battle ended in this call
                        await _notifier.NotifyBattleEndedAsync(
                            battleId,
                            battleEnded.Reason.ToString(),
                            battleEnded.WinnerPlayerId,
                            battleEnded.OccurredAt,
                            cancellationToken);

                        // Publish integration event via port
                        await _eventPublisher.PublishBattleEndedAsync(
                            battleId,
                            state.MatchId,
                            battleEnded.Reason,
                            battleEnded.WinnerPlayerId,
                            battleEnded.OccurredAt,
                            cancellationToken);

                        _logger.LogInformation(
                            "Battle {BattleId} ended. Reason: {Reason}, Winner: {WinnerPlayerId}",
                            battleId, battleEnded.Reason, battleEnded.WinnerPlayerId);
                    }
                    else if (endResult == EndBattleCommitResult.AlreadyEnded)
                    {
                        _logger.LogInformation(
                            "Battle {BattleId} already ended (duplicate ResolveTurn), skipping notifications",
                            battleId);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Battle {BattleId} could not be ended (NotCommitted). TurnIndex: {TurnIndex}",
                            battleId, turnIndex);
                    }
                    return true; // Battle ended (or already ended)

                case TurnResolvedDomainEvent turnResolved:
                    // Battle continues - open next turn
                    var nextTurnIndex = turnIndex + 1;
                    var turnSeconds = state.Ruleset.TurnSeconds;
                    var nextDeadline = _clock.UtcNow.AddSeconds(turnSeconds);

                    // Commit turn resolution + next turn opening atomically (includes HP update)
                    var nextTurnOpened = await _stateStore.MarkTurnResolvedAndOpenNextAsync(
                        battleId,
                        turnIndex,
                        nextTurnIndex,
                        nextDeadline,
                        resolutionResult.NewState.NoActionStreakBoth,
                        resolutionResult.NewState.PlayerA.CurrentHp,
                        resolutionResult.NewState.PlayerB.CurrentHp,
                        cancellationToken);

                    if (!nextTurnOpened)
                    {
                        _logger.LogError(
                            "Failed to open next turn {NextTurnIndex} for BattleId: {BattleId}",
                            nextTurnIndex, battleId);
                        return false;
                    }

                    // Reload state to get authoritative deadline
                    var stateAfterTurnOpen = await _stateStore.GetStateAsync(battleId, cancellationToken);
                    if (stateAfterTurnOpen == null)
                    {
                        _logger.LogError("Battle state disappeared after opening next turn for BattleId: {BattleId}", battleId);
                        return false;
                    }

                    var authoritativeNextDeadline = stateAfterTurnOpen.DeadlineUtc;

                    // Notify clients (only after successful commit) - pass domain-oriented parameters
                    var damageEvents = resolutionResult.Events.OfType<PlayerDamagedDomainEvent>().ToList();
                    foreach (var damageEvent in damageEvents)
                    {
                        await _notifier.NotifyPlayerDamagedAsync(
                            battleId,
                            damageEvent.PlayerId,
                            damageEvent.Damage,
                            damageEvent.RemainingHp,
                            damageEvent.TurnIndex,
                            cancellationToken);
                    }

                    await _notifier.NotifyTurnResolvedAsync(
                        battleId,
                        turnIndex,
                        FormatAction(turnResolved.PlayerAAction),
                        FormatAction(turnResolved.PlayerBAction),
                        cancellationToken);

                    await _notifier.NotifyTurnOpenedAsync(
                        battleId,
                        nextTurnIndex,
                        authoritativeNextDeadline,
                        cancellationToken);

                    await _notifier.NotifyBattleStateUpdatedAsync(
                        battleId,
                        stateAfterTurnOpen.PlayerAId,
                        stateAfterTurnOpen.PlayerBId,
                        stateAfterTurnOpen.Ruleset,
                        stateAfterTurnOpen.Phase.ToString(),
                        nextTurnIndex,
                        authoritativeNextDeadline,
                        resolutionResult.NewState.NoActionStreakBoth,
                        turnIndex,
                        null, // endedReason
                        stateAfterTurnOpen.Version,
                        resolutionResult.NewState.PlayerA.CurrentHp,
                        resolutionResult.NewState.PlayerB.CurrentHp,
                        cancellationToken);

                    _logger.LogInformation(
                        "Turn {TurnIndex} resolved and Turn {NextTurnIndex} opened for BattleId: {BattleId}. Next deadline: {DeadlineUtc}",
                        turnIndex, nextTurnIndex, battleId, authoritativeNextDeadline);
                    break;

                case PlayerDamagedDomainEvent:
                    // Already handled in TurnResolved case
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




