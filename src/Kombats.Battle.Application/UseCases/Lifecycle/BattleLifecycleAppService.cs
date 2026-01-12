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
    private readonly ICombatBalanceProvider _balanceProvider;
    private readonly IClock _clock;
    private readonly ILogger<BattleLifecycleAppService> _logger;

    public BattleLifecycleAppService(
        IBattleStateStore stateStore,
        IBattleRealtimeNotifier notifier,
        ICombatProfileProvider profileProvider,
        ICombatBalanceProvider balanceProvider,
        IClock clock,
        ILogger<BattleLifecycleAppService> logger)
    {
        _stateStore = stateStore;
        _notifier = notifier;
        _profileProvider = profileProvider;
        _balanceProvider = balanceProvider;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Handles BattleCreated event: initializes battle state and opens turn 1.
    /// Convergent and idempotent: re-processing the same event always converges to correct state.
    /// Never leaves battle in ArenaOpen without an active turn.
    /// 
    /// Uses a blind, convergent sequence:
    /// 1. TryInitializeBattleAsync (idempotent SETNX - ignore return value for flow decisions)
    /// 2. TryOpenTurnAsync(turnIndex=1) (idempotent Lua script - only succeeds if state exists, 
    ///    not ended, LastResolvedTurnIndex==0, and Phase is ArenaOpen or Resolving)
    /// 
    /// The TryOpenTurnScript is the convergence gate: it will return 0 if Turn 1 is already open
    /// (because Phase==TurnOpen and/or LastResolvedTurnIndex mismatch), so we only notify when
    /// it returns true (actual transition occurred).
    /// </summary>
    public async Task HandleBattleCreatedAsync(BattleCreated message, CancellationToken cancellationToken = default)
    {
        var battleId = message.BattleId;

        _logger.LogInformation(
            "Handling BattleCreated for BattleId: {BattleId}",
            battleId);

        // Validate ruleset - handle validation errors as non-retryable (log + return)
        var domainRuleset = ValidateRulesetOrReject(message.RulesetDto, battleId);
        if (domainRuleset == null)
        {
            // Validation failed - already logged, ACK message to avoid infinite retries
            return;
        }

        // Get player profiles (stats)
        var profileA = await _profileProvider.GetProfileAsync(message.PlayerAId, cancellationToken);
        var profileB = await _profileProvider.GetProfileAsync(message.PlayerBId, cancellationToken);
        
        if (profileA == null || profileB == null)
        {
            _logger.LogError(
                "Player profile not found for BattleId: {BattleId}, PlayerAId: {PlayerAId}, PlayerBId: {PlayerBId}. ACKing message to avoid infinite retries.",
                battleId, message.PlayerAId, message.PlayerBId);
            return;
        }

        // Build initial state
        var initialState = BuildInitialState(
            battleId,
            message.MatchId,
            message.PlayerAId,
            message.PlayerBId,
            domainRuleset,
            profileA,
            profileB);

        // Convergent initialization: blind call to TryInitializeBattleAsync
        // This is idempotent (SETNX) - ignore return value for flow decisions
        await _stateStore.TryInitializeBattleAsync(battleId, initialState, cancellationToken);

        // Convergent turn opening: blind call to TryOpenTurnAsync
        // TryOpenTurnScript is the convergence gate - it will:
        // - Return 1 only if: state exists, not ended, LastResolvedTurnIndex==0, Phase is ArenaOpen or Resolving
        // - Return 0 if Turn 1 is already open (Phase==TurnOpen and/or LastResolvedTurnIndex mismatch)
        var turn1Deadline = BuildTurn1DeadlineUtc(domainRuleset);
        var turnOpened = await _stateStore.TryOpenTurnAsync(battleId, 1, turn1Deadline, cancellationToken);

        // Only notify when Turn 1 was actually opened (TryOpenTurnAsync == true)
        // If it returns false, Turn 1 is already open or battle is in a different state (converged)
        if (turnOpened)
        {
            // Read state to get authoritative deadline (may differ slightly from our calculation)
            var state = await _stateStore.GetStateAsync(battleId, cancellationToken);
            if (state != null)
            {
                await _notifier.NotifyBattleReadyAsync(battleId, message.PlayerAId, message.PlayerBId, cancellationToken);
                await _notifier.NotifyTurnOpenedAsync(battleId, 1, state.DeadlineUtc, cancellationToken);

                _logger.LogInformation(
                    "Battle {BattleId} initialized and Turn 1 opened. Deadline: {DeadlineUtc}",
                    battleId, state.DeadlineUtc);
            }
        }
        else
        {
            _logger.LogInformation(
                "Battle {BattleId} already has Turn 1 open or is in a different state (converged, no notification sent)",
                battleId);
        }
    }

    /// <summary>
    /// Validates and creates Domain Ruleset from Contracts Ruleset.
    /// Returns null if validation fails (non-retryable error - log and ACK message).
    /// Throws only on transient infrastructure errors (should be retried).
    /// </summary>
    private Ruleset? ValidateRulesetOrReject(RulesetDto? contractRuleset, Guid battleId)
    {
        if (contractRuleset == null)
        {
            _logger.LogError(
                "Ruleset is null in BattleCreated event for BattleId: {BattleId}. This is a validation error. ACKing message to avoid infinite retries.",
                battleId);
            return null;
        }

        try
        {
            // Get CombatBalance from provider (required)
            var balance = _balanceProvider.GetBalance();

            // Create domain ruleset with strict validation
            // Ruleset.Create() will throw on invalid values (Version <= 0, TurnSeconds <= 0, etc.)
            return Ruleset.Create(
                version: contractRuleset.Version,
                turnSeconds: contractRuleset.TurnSeconds,
                noActionLimit: contractRuleset.NoActionLimit,
                seed: contractRuleset.Seed,
                balance: balance);
        }
        catch (ArgumentNullException ex)
        {
            // Validation error - non-retryable
            _logger.LogError(
                ex,
                "Missing required ruleset data in BattleCreated event for BattleId: {BattleId}. Validation error: {Error}. ACKing message to avoid infinite retries.",
                battleId, ex.Message);
            return null;
        }
        catch (ArgumentException ex)
        {
            // Validation error - non-retryable
            _logger.LogError(
                ex,
                "Invalid ruleset data in BattleCreated event for BattleId: {BattleId}. Validation error: {Error}. ACKing message to avoid infinite retries.",
                battleId, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Builds the initial battle domain state from message data and player profiles.
    /// Pure function - no I/O.
    /// </summary>
    private BattleDomainState BuildInitialState(
        Guid battleId,
        Guid matchId,
        Guid playerAId,
        Guid playerBId,
        Ruleset ruleset,
        CombatProfile profileA,
        CombatProfile profileB)
    {
        var playerAStats = new PlayerStats(profileA.Strength, profileA.Stamina, profileA.Agility, profileA.Intuition);
        var playerBStats = new PlayerStats(profileB.Strength, profileB.Stamina, profileB.Agility, profileB.Intuition);

        // Compute HP using CombatMath (ONCE at battle creation)
        var derivedA = CombatMath.ComputeDerived(playerAStats, ruleset.Balance);
        var derivedB = CombatMath.ComputeDerived(playerBStats, ruleset.Balance);

        var playerA = new PlayerState(playerAId, derivedA.HpMax, playerAStats);
        var playerB = new PlayerState(playerBId, derivedB.HpMax, playerBStats);

        return new BattleDomainState(
            battleId,
            matchId,
            playerAId,
            playerBId,
            ruleset,
            BattlePhase.ArenaOpen,
            turnIndex: 0,
            noActionStreakBoth: 0,
            lastResolvedTurnIndex: 0,
            playerA,
            playerB);
    }

    /// <summary>
    /// Builds the deadline for Turn 1 based on ruleset and current time.
    /// Pure function - no I/O.
    /// </summary>
    private DateTime BuildTurn1DeadlineUtc(Ruleset ruleset)
    {
        return _clock.UtcNow.AddSeconds(ruleset.TurnSeconds);
    }
}



