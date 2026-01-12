using Kombats.Battle.Application.Abstractions;
using Kombats.Battle.Application.ReadModels;
using Kombats.Battle.Application.UseCases.Lifecycle;
using Kombats.Battle.Domain.Model;
using Kombats.Battle.Domain.Rules;
using Kombats.Contracts.Battle;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Kombats.Battle.Application.Tests;

public class BattleLifecycleAppServiceTests
{
    private readonly Mock<IBattleStateStore> _stateStoreMock;
    private readonly Mock<IBattleRealtimeNotifier> _notifierMock;
    private readonly Mock<ICombatProfileProvider> _profileProviderMock;
    private readonly Mock<ICombatBalanceProvider> _balanceProviderMock;
    private readonly Mock<IClock> _clockMock;
    private readonly Mock<ILogger<BattleLifecycleAppService>> _loggerMock;
    private readonly BattleLifecycleAppService _service;

    public BattleLifecycleAppServiceTests()
    {
        _stateStoreMock = new Mock<IBattleStateStore>();
        _notifierMock = new Mock<IBattleRealtimeNotifier>();
        _profileProviderMock = new Mock<ICombatProfileProvider>();
        _balanceProviderMock = new Mock<ICombatBalanceProvider>();
        _clockMock = new Mock<IClock>();
        _loggerMock = new Mock<ILogger<BattleLifecycleAppService>>();

        _service = new BattleLifecycleAppService(
            _stateStoreMock.Object,
            _notifierMock.Object,
            _profileProviderMock.Object,
            _balanceProviderMock.Object,
            _clockMock.Object,
            _loggerMock.Object);
    }

    private static CombatBalance CreateTestBalance()
    {
        return new CombatBalance(
            hp: new HpBalance(baseHp: 100, hpPerEnd: 6),
            damage: new DamageBalance(
                baseWeaponDamage: 10,
                damagePerStr: 1.0m,
                damagePerAgi: 0.5m,
                damagePerInt: 0.3m,
                spreadMin: 0.85m,
                spreadMax: 1.15m),
            mf: new MfBalance(mfPerAgi: 5, mfPerInt: 5),
            dodgeChance: new ChanceBalance(
                @base: 0.05m,
                min: 0.02m,
                max: 0.35m,
                scale: 1.0m,
                kBase: 50m),
            critChance: new ChanceBalance(
                @base: 0.03m,
                min: 0.01m,
                max: 0.30m,
                scale: 1.0m,
                kBase: 60m),
            critEffect: new CritEffectBalance(
                mode: CritEffectMode.BypassBlock,
                multiplier: 1.5m,
                hybridBlockMultiplier: 0.5m));
    }

    private static BattleCreated CreateTestBattleCreated()
    {
        return new BattleCreated
        {
            BattleId = Guid.NewGuid(),
            MatchId = Guid.NewGuid(),
            PlayerAId = Guid.NewGuid(),
            PlayerBId = Guid.NewGuid(),
            RulesetDto = new RulesetDto
            {
                Version = 1,
                TurnSeconds = 30,
                NoActionLimit = 3,
                Seed = 42
            },
            State = "ArenaOpen",
            CreatedAt = DateTime.UtcNow,
            Version = 1
        };
    }

    [Fact]
    public async Task HandleBattleCreatedAsync_WhenInitSucceedsButTurnOpenFails_ShouldSucceedOnRetry()
    {
        // Arrange: Simulate the original production bug scenario
        // First attempt: init succeeds, but TryOpenTurnAsync fails transiently
        // Second attempt (retry): init is idempotent (returns false), but TryOpenTurnAsync succeeds
        var message = CreateTestBattleCreated();
        var battleId = message.BattleId;
        var now = DateTime.UtcNow;
        var deadline = now.AddSeconds(30);

        _clockMock.Setup(x => x.UtcNow).Returns(now);
        _balanceProviderMock.Setup(x => x.GetBalance()).Returns(CreateTestBalance());
        _profileProviderMock
            .Setup(x => x.GetProfileAsync(message.PlayerAId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CombatProfile(message.PlayerAId, 5, 5, 3, 2));
        _profileProviderMock
            .Setup(x => x.GetProfileAsync(message.PlayerBId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CombatProfile(message.PlayerBId, 5, 5, 3, 2));

        // First attempt: init succeeds, turn open fails (transient failure)
        _stateStoreMock
            .SetupSequence(x => x.TryInitializeBattleAsync(battleId, It.IsAny<BattleDomainState>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)  // First call: newly initialized
            .ReturnsAsync(false); // Second call (retry): already initialized

        _stateStoreMock
            .SetupSequence(x => x.TryOpenTurnAsync(battleId, 1, deadline, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false)  // First call: transient failure
            .ReturnsAsync(true);  // Second call (retry): succeeds

        // After successful turn open, state read returns TurnOpen state
        var stateAfterOpen = new BattleSnapshot
        {
            BattleId = battleId,
            MatchId = message.MatchId,
            PlayerAId = message.PlayerAId,
            PlayerBId = message.PlayerBId,
            Phase = BattlePhase.TurnOpen,
            TurnIndex = 1,
            LastResolvedTurnIndex = 0,
            Ruleset = Ruleset.Create(1, 30, 3, 42, CreateTestBalance()),
            DeadlineUtc = deadline,
            Version = 2,
            PlayerAHp = 130,
            PlayerBHp = 130,
            PlayerAStrength = 5,
            PlayerAStamina = 5,
            PlayerBStrength = 5,
            PlayerBStamina = 5
        };

        _stateStoreMock
            .Setup(x => x.GetStateAsync(battleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stateAfterOpen);

        // Act - First attempt (fails on turn open)
        await _service.HandleBattleCreatedAsync(message);

        // Verify first attempt: init was called, turn open failed, no notification
        _stateStoreMock.Verify(
            x => x.TryInitializeBattleAsync(battleId, It.IsAny<BattleDomainState>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _stateStoreMock.Verify(
            x => x.TryOpenTurnAsync(battleId, 1, deadline, It.IsAny<CancellationToken>()),
            Times.Once);
        _notifierMock.Verify(
            x => x.NotifyBattleReadyAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _notifierMock.Verify(
            x => x.NotifyTurnOpenedAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Act - Second attempt (retry: init is idempotent, turn open succeeds)
        await _service.HandleBattleCreatedAsync(message);

        // Verify second attempt: init called again (idempotent), turn open succeeds, notification sent
        _stateStoreMock.Verify(
            x => x.TryInitializeBattleAsync(battleId, It.IsAny<BattleDomainState>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        _stateStoreMock.Verify(
            x => x.TryOpenTurnAsync(battleId, 1, deadline, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        _notifierMock.Verify(
            x => x.NotifyBattleReadyAsync(battleId, message.PlayerAId, message.PlayerBId, It.IsAny<CancellationToken>()),
            Times.Once);
        _notifierMock.Verify(
            x => x.NotifyTurnOpenedAsync(battleId, 1, deadline, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleBattleCreatedAsync_WhenTurn1AlreadyOpen_ShouldNotReopenOrReNotify()
    {
        // Arrange: Battle already has Turn 1 open (idempotent redelivery scenario)
        var message = CreateTestBattleCreated();
        var battleId = message.BattleId;
        var now = DateTime.UtcNow;
        var deadline = now.AddSeconds(30);

        _clockMock.Setup(x => x.UtcNow).Returns(now);
        _balanceProviderMock.Setup(x => x.GetBalance()).Returns(CreateTestBalance());
        _profileProviderMock
            .Setup(x => x.GetProfileAsync(message.PlayerAId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CombatProfile(message.PlayerAId, 5, 5, 3, 2));
        _profileProviderMock
            .Setup(x => x.GetProfileAsync(message.PlayerBId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CombatProfile(message.PlayerBId, 5, 5, 3, 2));

        // Init is idempotent (returns false - already exists)
        _stateStoreMock
            .Setup(x => x.TryInitializeBattleAsync(battleId, It.IsAny<BattleDomainState>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Turn open is idempotent (returns false - Turn 1 already open)
        // TryOpenTurnScript returns 0 because Phase==TurnOpen and/or LastResolvedTurnIndex mismatch
        _stateStoreMock
            .Setup(x => x.TryOpenTurnAsync(battleId, 1, deadline, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        await _service.HandleBattleCreatedAsync(message);

        // Assert: No notifications sent (idempotent - Turn 1 already open)
        _stateStoreMock.Verify(
            x => x.TryInitializeBattleAsync(battleId, It.IsAny<BattleDomainState>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _stateStoreMock.Verify(
            x => x.TryOpenTurnAsync(battleId, 1, deadline, It.IsAny<CancellationToken>()),
            Times.Once);
        _notifierMock.Verify(
            x => x.NotifyBattleReadyAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _notifierMock.Verify(
            x => x.NotifyTurnOpenedAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleBattleCreatedAsync_WhenRulesetIsNull_ShouldLogErrorAndReturnWithoutThrowing()
    {
        // Arrange: Invalid ruleset (null)
        var message = CreateTestBattleCreated();
        message = message with { RulesetDto = null! };

        // Act & Assert: Should not throw, should return gracefully
        await _service.HandleBattleCreatedAsync(message);

        // Verify: No state operations attempted, no notifications
        _stateStoreMock.Verify(
            x => x.TryInitializeBattleAsync(It.IsAny<Guid>(), It.IsAny<BattleDomainState>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _stateStoreMock.Verify(
            x => x.TryOpenTurnAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _notifierMock.Verify(
            x => x.NotifyBattleReadyAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleBattleCreatedAsync_WhenRulesetHasInvalidVersion_ShouldLogErrorAndReturnWithoutThrowing()
    {
        // Arrange: Invalid ruleset (Version <= 0)
        var message = CreateTestBattleCreated();
        message = message with
        {
            RulesetDto = new RulesetDto
            {
                Version = 0,  // Invalid
                TurnSeconds = 30,
                NoActionLimit = 3,
                Seed = 42
            }
        };

        _balanceProviderMock.Setup(x => x.GetBalance()).Returns(CreateTestBalance());

        // Act & Assert: Should not throw, should return gracefully
        await _service.HandleBattleCreatedAsync(message);

        // Verify: No state operations attempted, no notifications
        _stateStoreMock.Verify(
            x => x.TryInitializeBattleAsync(It.IsAny<Guid>(), It.IsAny<BattleDomainState>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _stateStoreMock.Verify(
            x => x.TryOpenTurnAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _notifierMock.Verify(
            x => x.NotifyBattleReadyAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleBattleCreatedAsync_WhenRulesetHasInvalidTurnSeconds_ShouldLogErrorAndReturnWithoutThrowing()
    {
        // Arrange: Invalid ruleset (TurnSeconds <= 0)
        var message = CreateTestBattleCreated();
        message = message with
        {
            RulesetDto = new RulesetDto
            {
                Version = 1,
                TurnSeconds = 0,  // Invalid
                NoActionLimit = 3,
                Seed = 42
            }
        };

        _balanceProviderMock.Setup(x => x.GetBalance()).Returns(CreateTestBalance());

        // Act & Assert: Should not throw, should return gracefully
        await _service.HandleBattleCreatedAsync(message);

        // Verify: No state operations attempted, no notifications
        _stateStoreMock.Verify(
            x => x.TryInitializeBattleAsync(It.IsAny<Guid>(), It.IsAny<BattleDomainState>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _stateStoreMock.Verify(
            x => x.TryOpenTurnAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _notifierMock.Verify(
            x => x.NotifyBattleReadyAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleBattleCreatedAsync_WhenProfileIsNull_ShouldLogErrorAndReturnWithoutThrowing()
    {
        // Arrange: Profile not found (null)
        var message = CreateTestBattleCreated();
        var battleId = message.BattleId;

        _clockMock.Setup(x => x.UtcNow).Returns(DateTime.UtcNow);
        _balanceProviderMock.Setup(x => x.GetBalance()).Returns(CreateTestBalance());
        _profileProviderMock
            .Setup(x => x.GetProfileAsync(message.PlayerAId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CombatProfile?)null);  // Profile not found

        // Act & Assert: Should not throw, should return gracefully
        await _service.HandleBattleCreatedAsync(message);

        // Verify: No state operations attempted, no notifications
        _stateStoreMock.Verify(
            x => x.TryInitializeBattleAsync(It.IsAny<Guid>(), It.IsAny<BattleDomainState>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _stateStoreMock.Verify(
            x => x.TryOpenTurnAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _notifierMock.Verify(
            x => x.NotifyBattleReadyAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleBattleCreatedAsync_WhenHappyPath_ShouldInitializeAndOpenTurn1()
    {
        // Arrange: Happy path - all operations succeed
        var message = CreateTestBattleCreated();
        var battleId = message.BattleId;
        var now = DateTime.UtcNow;
        var deadline = now.AddSeconds(30);

        _clockMock.Setup(x => x.UtcNow).Returns(now);
        _balanceProviderMock.Setup(x => x.GetBalance()).Returns(CreateTestBalance());
        _profileProviderMock
            .Setup(x => x.GetProfileAsync(message.PlayerAId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CombatProfile(message.PlayerAId, 5, 5, 3, 2));
        _profileProviderMock
            .Setup(x => x.GetProfileAsync(message.PlayerBId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CombatProfile(message.PlayerBId, 5, 5, 3, 2));

        _stateStoreMock
            .Setup(x => x.TryInitializeBattleAsync(battleId, It.IsAny<BattleDomainState>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _stateStoreMock
            .Setup(x => x.TryOpenTurnAsync(battleId, 1, deadline, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var stateAfterOpen = new BattleSnapshot
        {
            BattleId = battleId,
            MatchId = message.MatchId,
            PlayerAId = message.PlayerAId,
            PlayerBId = message.PlayerBId,
            Phase = BattlePhase.TurnOpen,
            TurnIndex = 1,
            LastResolvedTurnIndex = 0,
            Ruleset = Ruleset.Create(1, 30, 3, 42, CreateTestBalance()),
            DeadlineUtc = deadline,
            Version = 2,
            PlayerAHp = 130,
            PlayerBHp = 130,
            PlayerAStrength = 5,
            PlayerAStamina = 5,
            PlayerBStrength = 5,
            PlayerBStamina = 5
        };

        _stateStoreMock
            .Setup(x => x.GetStateAsync(battleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stateAfterOpen);

        // Act
        await _service.HandleBattleCreatedAsync(message);

        // Assert: Both operations called, notifications sent
        _stateStoreMock.Verify(
            x => x.TryInitializeBattleAsync(battleId, It.IsAny<BattleDomainState>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _stateStoreMock.Verify(
            x => x.TryOpenTurnAsync(battleId, 1, deadline, It.IsAny<CancellationToken>()),
            Times.Once);
        _notifierMock.Verify(
            x => x.NotifyBattleReadyAsync(battleId, message.PlayerAId, message.PlayerBId, It.IsAny<CancellationToken>()),
            Times.Once);
        _notifierMock.Verify(
            x => x.NotifyTurnOpenedAsync(battleId, 1, deadline, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}

