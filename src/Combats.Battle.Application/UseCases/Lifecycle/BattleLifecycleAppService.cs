using Combats.Battle.Application.Abstractions;
using Combats.Battle.Domain.Model;
using Combats.Battle.Domain.Rules;
using Combats.Contracts.Battle;
using Microsoft.Extensions.Logging;

namespace Combats.Battle.Application.UseCases.Lifecycle;

/// <summary>
/// Application service for battle lifecycle operations.
/// Orchestrates battle initialization and turn opening.
/// </summary>
public class BattleLifecycleAppService
{
    private readonly IBattleStateStore _stateStore;
    private readonly IBattleRealtimeNotifier _notifier;
    private readonly ICombatProfileProvider _profileProvider;
    private readonly IClock _clock;
    private readonly RulesetNormalizer _rulesetNormalizer;
    private readonly ILogger<BattleLifecycleAppService> _logger;

    public BattleLifecycleAppService(
        IBattleStateStore stateStore,
        IBattleRealtimeNotifier notifier,
        ICombatProfileProvider profileProvider,
        IClock clock,
        RulesetNormalizer rulesetNormalizer,
        ILogger<BattleLifecycleAppService> logger)
    {
        _stateStore = stateStore;
        _notifier = notifier;
        _profileProvider = profileProvider;
        _clock = clock;
        _rulesetNormalizer = rulesetNormalizer;
        _logger = logger;
    }

    /// <summary>
    /// Handles BattleCreated event: initializes battle state and opens turn 1.
    /// Idempotent: if battle already initialized, returns without error.
    /// </summary>
    public async Task HandleBattleCreatedAsync(BattleCreated message, CancellationToken cancellationToken = default)
    {
        var battleId = message.BattleId;

        _logger.LogInformation(
            "Handling BattleCreated for BattleId: {BattleId}",
            battleId);

        // Get player profiles (stats)
        var profileA = await _profileProvider.GetProfileAsync(message.PlayerAId, cancellationToken);
        var profileB = await _profileProvider.GetProfileAsync(message.PlayerBId, cancellationToken);

        // Normalize ruleset (applies defaults and enforces bounds) - single source of truth
        var normalizedRuleset = _rulesetNormalizer.Normalize(message.Ruleset);

        // Use defaults if profile not found (should not happen, but defensive)
        var strengthA = profileA?.Strength ?? 10;
        var staminaA = profileA?.Stamina ?? 10;
        var strengthB = profileB?.Strength ?? 10;
        var staminaB = profileB?.Stamina ?? 10;

        // Use normalized ruleset for HP calculation
        var hpPerStamina = normalizedRuleset.HpPerStamina;
        var initialMaxHpA = staminaA * hpPerStamina;
        var initialMaxHpB = staminaB * hpPerStamina;

        // Create domain state
        var playerAStats = new PlayerStats(strengthA, staminaA);
        var playerBStats = new PlayerStats(strengthB, staminaB);
        var playerA = new PlayerState(message.PlayerAId, initialMaxHpA, playerAStats);
        var playerB = new PlayerState(message.PlayerBId, initialMaxHpB, playerBStats);

        var initialState = new BattleDomainState(
            battleId,
            message.MatchId,
            message.PlayerAId,
            message.PlayerBId,
            normalizedRuleset,
            BattlePhase.ArenaOpen,
            turnIndex: 0,
            noActionStreakBoth: 0,
            lastResolvedTurnIndex: 0,
            playerA,
            playerB);

        // Idempotent initialization
        var initialized = await _stateStore.TryInitializeBattleAsync(battleId, initialState, cancellationToken);
        if (!initialized)
        {
            _logger.LogInformation(
                "Battle {BattleId} already initialized, skipping (idempotent behavior)",
                battleId);
            return;
        }

        // Open Turn 1 - use normalized ruleset
        var turnSeconds = normalizedRuleset.TurnSeconds;
        var turn1Deadline = _clock.UtcNow.AddSeconds(turnSeconds);

        var turnOpened = await _stateStore.TryOpenTurnAsync(battleId, 1, turn1Deadline, cancellationToken);
        if (!turnOpened)
        {
            _logger.LogWarning(
                "Failed to open Turn 1 for BattleId: {BattleId}",
                battleId);
            return;
        }

        // Reload state to get authoritative deadline
        var state = await _stateStore.GetStateAsync(battleId, cancellationToken);
        if (state == null)
        {
            _logger.LogError("Battle state disappeared after opening Turn 1 for BattleId: {BattleId}", battleId);
            return;
        }

        var authoritativeDeadline = state.DeadlineUtc;

        // Notify clients
        await _notifier.NotifyBattleReadyAsync(battleId, message.PlayerAId, message.PlayerBId, cancellationToken);
        await _notifier.NotifyTurnOpenedAsync(battleId, 1, authoritativeDeadline, cancellationToken);

        _logger.LogInformation(
            "Battle {BattleId} initialized and Turn 1 opened. Deadline: {DeadlineUtc}",
            battleId, authoritativeDeadline);
    }
}



