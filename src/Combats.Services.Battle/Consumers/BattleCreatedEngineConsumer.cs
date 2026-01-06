using MassTransit;
using Combats.Contracts.Battle;
using Combats.Services.Battle.State;
using Combats.Services.Battle.DTOs;
using Combats.Services.Battle.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Combats.Services.Battle.Consumers;

public class BattleCreatedEngineConsumer : IConsumer<BattleCreated>
{
    private readonly IBattleStateStore _stateStore;
    private readonly IHubContext<BattleHub> _hubContext;
    private readonly ILogger<BattleCreatedEngineConsumer> _logger;

    public BattleCreatedEngineConsumer(
        IBattleStateStore stateStore,
        IHubContext<BattleHub> hubContext,
        ILogger<BattleCreatedEngineConsumer> logger)
    {
        _stateStore = stateStore;
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<BattleCreated> context)
    {
        var message = context.Message;
        var battleId = message.BattleId;

        _logger.LogInformation(
            "Processing BattleCreated event for BattleId: {BattleId}, MessageId: {MessageId}, CorrelationId: {CorrelationId}",
            battleId, context.MessageId, context.CorrelationId);

        // Idempotency guard: try to initialize battle state
        // Initialize with default stats (Strength=10, Stamina=10) for both players
        const int defaultStrength = 10;
        const int defaultStamina = 10;
        var hpPerStamina = message.Ruleset.HpPerStamina > 0 ? message.Ruleset.HpPerStamina : 10;
        var initialMaxHpA = defaultStamina * hpPerStamina;
        var initialMaxHpB = defaultStamina * hpPerStamina;
        
        var initialState = new BattleState
        {
            BattleId = battleId,
            MatchId = message.MatchId,
            PlayerAId = message.PlayerAId,
            PlayerBId = message.PlayerBId,
            Ruleset = message.Ruleset,
            Phase = BattlePhase.ArenaOpen,
            TurnIndex = 0,
            NoActionStreakBoth = 0,
            LastResolvedTurnIndex = 0,
            Version = 1,
            PlayerAHp = initialMaxHpA,
            PlayerBHp = initialMaxHpB,
            PlayerAStrength = defaultStrength,
            PlayerAStamina = defaultStamina,
            PlayerBStrength = defaultStrength,
            PlayerBStamina = defaultStamina
        };
        // ArenaOpen phase deadline is set to now (meaningless but consistent)
        initialState.SetDeadlineUtc(DateTime.UtcNow);

        var initialized = await _stateStore.TryInitializeBattleAsync(battleId, initialState, context.CancellationToken);
        if (!initialized)
        {
            _logger.LogInformation(
                "Battle {BattleId} already initialized, skipping (idempotent behavior)",
                battleId);
            return;
        }

        // Compute single authoritative deadline for Turn 1 using TurnSeconds from Ruleset
        var turnSeconds = message.Ruleset.TurnSeconds;
        var turn1Deadline = DateTime.UtcNow.AddSeconds(turnSeconds);

        // Open Turn 1 with the computed deadline (stored in state)
        var turnOpened = await _stateStore.TryOpenTurnAsync(battleId, 1, turn1Deadline, context.CancellationToken);
        if (!turnOpened)
        {
            _logger.LogWarning(
                "Failed to open Turn 1 for BattleId: {BattleId}. DO NOT proceeding to scheduling.",
                battleId);
            return;
        }

        // Reload state to get the stored deadline (authoritative source)
        var state = await _stateStore.GetStateAsync(battleId, context.CancellationToken);
        if (state == null)
        {
            _logger.LogError("Battle state disappeared after opening Turn 1 for BattleId: {BattleId}", battleId);
            return;
        }

        // Use stored deadline from state (authoritative)
        var authoritativeDeadline = state.GetDeadlineUtc();

        // Notify clients via SignalR with typed DTOs using authoritative deadline
        await _hubContext.Clients.Group($"battle:{battleId}").SendAsync("BattleReady", new BattleReadyDto
        {
            BattleId = battleId,
            PlayerAId = message.PlayerAId,
            PlayerBId = message.PlayerBId
        }, context.CancellationToken);

        await _hubContext.Clients.Group($"battle:{battleId}").SendAsync("TurnOpened", new TurnOpenedDto
        {
            BattleId = battleId,
            TurnIndex = 1,
            DeadlineUtc = authoritativeDeadline.ToUniversalTime().ToString("O")
        }, context.CancellationToken);

        // Note: TryOpenTurnAsync already added battle to deadlines ZSET, so TurnDeadlineWorker will handle resolution
        _logger.LogInformation(
            "Battle {BattleId} initialized and Turn 1 opened. Deadline: {DeadlineUtc}. TurnDeadlineWorker will resolve at deadline.",
            battleId, authoritativeDeadline);
    }
}
