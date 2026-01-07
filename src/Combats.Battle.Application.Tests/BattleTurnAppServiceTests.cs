using Combats.Battle.Application.Ports;
using Combats.Battle.Application.Services;
using Combats.Battle.Application.Validation;
using Combats.Battle.Domain.Engine;
using Combats.Battle.Domain.Events;
using Combats.Battle.Domain.Model;
using Combats.Contracts.Battle;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Combats.Battle.Application.Tests;

public class BattleTurnAppServiceTests
{
    private readonly Mock<IBattleStateStore> _stateStoreMock;
    private readonly Mock<IBattleEngine> _battleEngineMock;
    private readonly Mock<IBattleRealtimeNotifier> _notifierMock;
    private readonly Mock<IBattleEventPublisher> _eventPublisherMock;
    private readonly PlayerActionNormalizer _actionNormalizer;
    private readonly Mock<IClock> _clockMock;
    private readonly Mock<ILogger<BattleTurnAppService>> _loggerMock;
    private readonly BattleTurnAppService _service;

    public BattleTurnAppServiceTests()
    {
        _stateStoreMock = new Mock<IBattleStateStore>();
        _battleEngineMock = new Mock<IBattleEngine>();
        _notifierMock = new Mock<IBattleRealtimeNotifier>();
        _eventPublisherMock = new Mock<IBattleEventPublisher>();
        _clockMock = new Mock<IClock>();
	_actionNormalizer = new PlayerActionNormalizer(
		_clockMock.Object,
		Mock.Of<ILogger<PlayerActionNormalizer>>());
        _loggerMock = new Mock<ILogger<BattleTurnAppService>>();

        _service = new BattleTurnAppService(
            _stateStoreMock.Object,
            _battleEngineMock.Object,
            _notifierMock.Object,
            _eventPublisherMock.Object,
            _actionNormalizer,
            _clockMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task ResolveTurnAsync_WhenEndBattleReturnsEndedNow_ShouldNotifyAndPublish()
    {
        // Arrange
        var battleId = Guid.NewGuid();
        var matchId = Guid.NewGuid();
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();
        var turnIndex = 5;
        var occurredAt = DateTime.UtcNow;
        var winnerId = playerAId;

		var state = new BattleStateView
        {
            BattleId = battleId,
            MatchId = matchId,
            PlayerAId = playerAId,
            PlayerBId = playerBId,
			Phase = BattlePhaseView.TurnOpen,
            TurnIndex = turnIndex,
            LastResolvedTurnIndex = turnIndex - 1,
            Ruleset = new Ruleset { TurnSeconds = 30 }
        };
		var stateAfterCas = new BattleStateView
		{
			BattleId = battleId,
			MatchId = matchId,
			PlayerAId = playerAId,
			PlayerBId = playerBId,
			Phase = BattlePhaseView.Resolving,
			TurnIndex = turnIndex,
			LastResolvedTurnIndex = turnIndex - 1,
			Ruleset = new Ruleset { TurnSeconds = 30 }
		};

		var ruleset = new Combats.Contracts.Battle.Ruleset { Version = 1, TurnSeconds = 30, NoActionLimit = 3, Seed = 42 };
		var playerAState = new PlayerState(playerAId, 100, 80, new PlayerStats(5, 5));
		var playerBState = new PlayerState(playerBId, 100, 70, new PlayerStats(5, 5));
		var domainState = new BattleDomainState(battleId, matchId, playerAId, playerBId, ruleset, BattlePhase.Resolving, turnIndex, 0, turnIndex - 1, playerAState, playerBState);

        var battleEndedEvent = new BattleEndedDomainEvent(
            battleId,
            winnerId,
            BattleEndReason.Normal,
            turnIndex,
            occurredAt);

		var resolutionResult = new BattleResolutionResult
		{
			NewState = domainState,
			Events = new List<IDomainEvent> { battleEndedEvent }
		};

		_stateStoreMock
			.SetupSequence(x => x.GetStateAsync(battleId, It.IsAny<CancellationToken>()))
			.ReturnsAsync(state)
			.ReturnsAsync(stateAfterCas);

        _stateStoreMock
            .Setup(x => x.TryMarkTurnResolvingAsync(battleId, turnIndex, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _stateStoreMock
            .Setup(x => x.GetActionsAsync(battleId, turnIndex, playerAId, playerBId, It.IsAny<CancellationToken>()))
			.ReturnsAsync((PlayerAAction: "", PlayerBAction: ""));

        _battleEngineMock
            .Setup(x => x.ResolveTurn(It.IsAny<BattleDomainState>(), It.IsAny<PlayerAction>(), It.IsAny<PlayerAction>()))
            .Returns(resolutionResult);

        _stateStoreMock
            .Setup(x => x.EndBattleAndMarkResolvedAsync(
                battleId,
                turnIndex,
                0,
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(EndBattleCommitResult.EndedNow);

		// Act
		var result = await _service.ResolveTurnAsync(battleId);

		// Assert (behavioral - don't assert return value semantics here)
        _notifierMock.Verify(
            x => x.NotifyBattleEndedAsync(
                battleId,
                BattleEndReason.Normal.ToString(),
                winnerId,
                occurredAt,
                It.IsAny<CancellationToken>()),
            Times.Once);
        _eventPublisherMock.Verify(
            x => x.PublishBattleEndedAsync(
                battleId,
                matchId,
                BattleEndReason.Normal,
                winnerId,
                occurredAt,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ResolveTurnAsync_WhenEndBattleReturnsAlreadyEnded_ShouldNotNotifyOrPublish()
    {
        // Arrange
        var battleId = Guid.NewGuid();
        var matchId = Guid.NewGuid();
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();
        var turnIndex = 5;
        var occurredAt = DateTime.UtcNow;
        var winnerId = playerAId;

		var state = new BattleStateView
        {
            BattleId = battleId,
            MatchId = matchId,
            PlayerAId = playerAId,
            PlayerBId = playerBId,
			Phase = BattlePhaseView.TurnOpen,
            TurnIndex = turnIndex,
            LastResolvedTurnIndex = turnIndex - 1,
            Ruleset = new Ruleset { TurnSeconds = 30 }
        };
		var stateAfterCas = new BattleStateView
		{
			BattleId = battleId,
			MatchId = matchId,
			PlayerAId = playerAId,
			PlayerBId = playerBId,
			Phase = BattlePhaseView.Resolving,
			TurnIndex = turnIndex,
			LastResolvedTurnIndex = turnIndex - 1,
			Ruleset = new Ruleset { TurnSeconds = 30 }
		};

		var ruleset = new Combats.Contracts.Battle.Ruleset { Version = 1, TurnSeconds = 30, NoActionLimit = 3, Seed = 42 };
		var playerAState = new PlayerState(playerAId, 100, 80, new PlayerStats(5, 5));
		var playerBState = new PlayerState(playerBId, 100, 70, new PlayerStats(5, 5));
		var domainState = new BattleDomainState(battleId, matchId, playerAId, playerBId, ruleset, BattlePhase.Resolving, turnIndex, 0, turnIndex - 1, playerAState, playerBState);

        var battleEndedEvent = new BattleEndedDomainEvent(
            battleId,
            winnerId,
            BattleEndReason.Normal,
            turnIndex,
            occurredAt);

		var resolutionResult = new BattleResolutionResult
		{
			NewState = domainState,
			Events = new List<IDomainEvent> { battleEndedEvent }
		};

		_stateStoreMock
			.SetupSequence(x => x.GetStateAsync(battleId, It.IsAny<CancellationToken>()))
			.ReturnsAsync(state)
			.ReturnsAsync(stateAfterCas);

        _stateStoreMock
            .Setup(x => x.TryMarkTurnResolvingAsync(battleId, turnIndex, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _stateStoreMock
            .Setup(x => x.GetActionsAsync(battleId, turnIndex, playerAId, playerBId, It.IsAny<CancellationToken>()))
			.ReturnsAsync((PlayerAAction: "", PlayerBAction: ""));

        _battleEngineMock
            .Setup(x => x.ResolveTurn(It.IsAny<BattleDomainState>(), It.IsAny<PlayerAction>(), It.IsAny<PlayerAction>()))
            .Returns(resolutionResult);

        _stateStoreMock
            .Setup(x => x.EndBattleAndMarkResolvedAsync(
                battleId,
                turnIndex,
                0,
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(EndBattleCommitResult.AlreadyEnded);

		// Act
		var result = await _service.ResolveTurnAsync(battleId);

		// Assert (behavioral - don't assert return value semantics here)
        _notifierMock.Verify(
            x => x.NotifyBattleEndedAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<Guid?>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _eventPublisherMock.Verify(
            x => x.PublishBattleEndedAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<BattleEndReason>(),
                It.IsAny<Guid?>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SubmitActionAsync_WhenStoreActionReturnsAlreadySubmitted_ShouldNotThrow()
    {
        // Arrange
        var battleId = Guid.NewGuid();
		var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();
		var playerId = playerAId;
        var turnIndex = 5;

        var now = DateTime.UtcNow;
        var deadline = now.AddSeconds(30);
        _clockMock.Setup(x => x.UtcNow).Returns(now);

		var state = new BattleStateView
        {
            BattleId = battleId,
			PlayerAId = playerId,
			PlayerBId = playerId,
            Phase = BattlePhaseView.TurnOpen,
            TurnIndex = turnIndex,
            LastResolvedTurnIndex = turnIndex - 1,
            DeadlineUtc = deadline,
            Ruleset = new Ruleset { TurnSeconds = 30 }
        };

        _stateStoreMock
            .Setup(x => x.GetStateAsync(battleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        _stateStoreMock
            .Setup(x => x.StoreActionAsync(battleId, turnIndex, playerId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActionStoreResult.AlreadySubmitted);

        _stateStoreMock
            .Setup(x => x.GetActionsAsync(battleId, turnIndex, playerAId, playerBId, It.IsAny<CancellationToken>()))
			.ReturnsAsync((PlayerAAction: (string?)null, PlayerBAction: (string?)null));

        // Act & Assert - should not throw
		var validActionPayload1 = "{\"attackZone\":\"head\"}";
		await _service.SubmitActionAsync(battleId, playerId, turnIndex, validActionPayload1);
    }

    [Fact]
    public async Task SubmitActionAsync_WhenStoreActionReturnsAccepted_ShouldProceedNormally()
    {
        // Arrange
        var battleId = Guid.NewGuid();
		var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();
		var playerId = playerAId;
        var turnIndex = 5;

        var now = DateTime.UtcNow;
        var deadline = now.AddSeconds(30);
        _clockMock.Setup(x => x.UtcNow).Returns(now);

		var state = new BattleStateView
        {
            BattleId = battleId,
			PlayerAId = playerId,
			PlayerBId = playerId,
            Phase = BattlePhaseView.TurnOpen,
            TurnIndex = turnIndex,
            LastResolvedTurnIndex = turnIndex - 1,
            DeadlineUtc = deadline,
            Ruleset = new Ruleset { TurnSeconds = 30 }
        };

        _stateStoreMock
            .Setup(x => x.GetStateAsync(battleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        _stateStoreMock
            .Setup(x => x.StoreActionAsync(battleId, turnIndex, playerId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActionStoreResult.Accepted);

        _stateStoreMock
            .Setup(x => x.GetActionsAsync(battleId, turnIndex, playerAId, playerBId, It.IsAny<CancellationToken>()))
			.ReturnsAsync((PlayerAAction: (string?)null, PlayerBAction: (string?)null));

        // Act & Assert - should not throw
		var validActionPayload = "{\"attackZone\":\"head\"}";
		await _service.SubmitActionAsync(battleId, playerId, turnIndex, validActionPayload);

        _stateStoreMock.Verify(
            x => x.StoreActionAsync(battleId, turnIndex, playerId, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}

