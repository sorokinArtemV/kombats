using Combats.Battle.Application.Ports;
using Combats.Battle.Domain;
using Combats.Contracts.Battle;
using Microsoft.Extensions.Logging;

namespace Combats.Battle.Application.Services;

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
    private readonly ILogger<BattleLifecycleAppService> _logger;

    public BattleLifecycleAppService(
        IBattleStateStore stateStore,
        IBattleRealtimeNotifier notifier,
        ICombatProfileProvider profileProvider,
        IClock clock,
        ILogger<BattleLifecycleAppService> logger)
    {
        _stateStore = stateStore;
        _notifier = notifier;
        _profileProvider = profileProvider;
        _clock = clock;
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

        // Use defaults if profile not found (should not happen, but defensive)
        var strengthA = profileA?.Strength ?? 10;
        var staminaA = profileA?.Stamina ?? 10;
        var strengthB = profileB?.Strength ?? 10;
        var staminaB = profileB?.Stamina ?? 10;

        var hpPerStamina = message.Ruleset.HpPerStamina > 0 ? message.Ruleset.HpPerStamina : 10;
        var initialMaxHpA = staminaA * hpPerStamina;
        var initialMaxHpB = staminaB * hpPerStamina;

        var initialState = new BattleStateView
        {
            BattleId = battleId,
            MatchId = message.MatchId,
            PlayerAId = message.PlayerAId,
            PlayerBId = message.PlayerBId,
            Ruleset = message.Ruleset,
            Phase = BattlePhaseView.ArenaOpen,
            TurnIndex = 0,
            DeadlineUtc = _clock.UtcNow, // ArenaOpen deadline is meaningless but consistent
            NoActionStreakBoth = 0,
            LastResolvedTurnIndex = 0,
            Version = 1,
            PlayerAHp = initialMaxHpA,
            PlayerBHp = initialMaxHpB,
            PlayerAStrength = strengthA,
            PlayerAStamina = staminaA,
            PlayerBStrength = strengthB,
            PlayerBStamina = staminaB
        };

        // Idempotent initialization
        var initialized = await _stateStore.TryInitializeBattleAsync(battleId, initialState, cancellationToken);
        if (!initialized)
        {
            _logger.LogInformation(
                "Battle {BattleId} already initialized, skipping (idempotent behavior)",
                battleId);
            return;
        }

        // Open Turn 1
        var turnSeconds = message.Ruleset.TurnSeconds;
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


