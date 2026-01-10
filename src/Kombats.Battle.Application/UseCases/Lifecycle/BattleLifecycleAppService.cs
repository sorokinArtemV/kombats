using Kombats.Battle.Domain.Model;
using Kombats.Battle.Domain.Rules;
using Kombats.Contracts.Battle;
using Kombats.Battle.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Kombats.Battle.Application.UseCases.Lifecycle;

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
        
        var strengthA = profileA.Strength;
        var staminaA = profileA.Stamina;
        var agilityA = profileA.Agility ;
        var intuitionA = profileA.Intuition ;
        var strengthB = profileB.Strength;
        var staminaB = profileB.Stamina;
        var agilityB = profileB.Agility;
        var intuitionB = profileB.Intuition;

        // Create domain state
        var playerAStats = new PlayerStats(strengthA, staminaA, agilityA, intuitionA);
        var playerBStats = new PlayerStats(strengthB, staminaB, agilityB, intuitionB);

        // Compute HP using CombatMath (ONCE at battle creation)
        var derivedA = CombatMath.ComputeDerived(playerAStats, normalizedRuleset.Balance);
        var derivedB = CombatMath.ComputeDerived(playerBStats, normalizedRuleset.Balance);

        var playerA = new PlayerState(message.PlayerAId, derivedA.HpMax, playerAStats);
        var playerB = new PlayerState(message.PlayerBId, derivedB.HpMax, playerBStats);

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



