using FluentAssertions;
using Kombats.Matchmaking.Application.Abstractions;
using Kombats.Matchmaking.Application.UseCases;
using Kombats.Matchmaking.Domain;
using Microsoft.Extensions.Logging;
using Moq;
using Match = Kombats.Matchmaking.Domain.Match;
using Xunit;

namespace Kombats.Matchmaking.Application.Tests;

public class QueueServiceTests
{
    private readonly Mock<IMatchQueueStore> _queueStoreMock;
    private readonly Mock<IMatchRepository> _matchRepositoryMock;
    private readonly Mock<ILogger<QueueService>> _loggerMock;
    private readonly QueueService _queueService;
    private const string DefaultVariant = "default";

    public QueueServiceTests()
    {
        _queueStoreMock = new Mock<IMatchQueueStore>();
        _matchRepositoryMock = new Mock<IMatchRepository>();
        _loggerMock = new Mock<ILogger<QueueService>>();
        _queueService = new QueueService(
            _queueStoreMock.Object,
            _matchRepositoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task GetStatusAsync_WhenPlayerHasActiveMatch_ReturnsInMatch()
    {
        // Arrange
        var playerId = Guid.NewGuid();
        var matchId = Guid.NewGuid();
        var battleId = Guid.NewGuid();
        var match = new Match
        {
            MatchId = matchId,
            BattleId = battleId,
            PlayerAId = playerId,
            PlayerBId = Guid.NewGuid(),
            Variant = DefaultVariant,
            State = MatchState.BattleCreateRequested,
            CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
            UpdatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        };

        _matchRepositoryMock
            .Setup(x => x.GetLatestForPlayerAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);
        _queueStoreMock
            .Setup(x => x.IsQueuedAsync(DefaultVariant, playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _queueService.GetStatusAsync(playerId);

        // Assert
        result.Should().NotBeNull();
        result!.State.Should().Be(PlayerMatchState.Matched);
        result.MatchId.Should().Be(matchId);
        result.BattleId.Should().Be(battleId);
        result.MatchState.Should().Be(MatchState.BattleCreateRequested);
    }

    [Fact]
    public async Task GetStatusAsync_WhenPlayerIsQueued_ReturnsSearching()
    {
        // Arrange
        var playerId = Guid.NewGuid();

        _matchRepositoryMock
            .Setup(x => x.GetLatestForPlayerAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Match?)null);
        _queueStoreMock
            .Setup(x => x.IsQueuedAsync(DefaultVariant, playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _queueService.GetStatusAsync(playerId);

        // Assert
        result.Should().NotBeNull();
        result!.State.Should().Be(PlayerMatchState.Searching);
        result.MatchId.Should().BeNull();
        result.BattleId.Should().BeNull();
        result.Variant.Should().Be(DefaultVariant);
    }

    [Fact]
    public async Task GetStatusAsync_WhenPlayerIsNotQueuedAndNoMatch_ReturnsIdle()
    {
        // Arrange
        var playerId = Guid.NewGuid();

        _matchRepositoryMock
            .Setup(x => x.GetLatestForPlayerAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Match?)null);
        _queueStoreMock
            .Setup(x => x.IsQueuedAsync(DefaultVariant, playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _queueService.GetStatusAsync(playerId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task JoinQueueAsync_WhenCalledTwice_IsIdempotent()
    {
        // Arrange
        var playerId = Guid.NewGuid();

        _matchRepositoryMock
            .Setup(x => x.GetLatestForPlayerAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Match?)null);
        _queueStoreMock
            .SetupSequence(x => x.IsQueuedAsync(DefaultVariant, playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false)  // First call: not queued
            .ReturnsAsync(true);   // Second call: already queued
        _queueStoreMock
            .Setup(x => x.TryJoinQueueAsync(DefaultVariant, playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result1 = await _queueService.JoinQueueAsync(playerId, DefaultVariant);
        var result2 = await _queueService.JoinQueueAsync(playerId, DefaultVariant);

        // Assert
        result1.State.Should().Be(PlayerMatchState.Searching);
        result2.State.Should().Be(PlayerMatchState.Searching);
        
        // Verify TryJoinQueueAsync was called only once (first call)
        _queueStoreMock.Verify(
            x => x.TryJoinQueueAsync(DefaultVariant, playerId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task LeaveQueueAsync_WhenCalledTwice_IsIdempotent()
    {
        // Arrange
        var playerId = Guid.NewGuid();

        _matchRepositoryMock
            .Setup(x => x.GetLatestForPlayerAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Match?)null);
        _queueStoreMock
            .SetupSequence(x => x.IsQueuedAsync(DefaultVariant, playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)   // First call: queued
            .ReturnsAsync(false);  // Second call: not queued (already left)
        _queueStoreMock
            .Setup(x => x.TryLeaveQueueAsync(DefaultVariant, playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result1 = await _queueService.LeaveQueueAsync(playerId, DefaultVariant);
        var result2 = await _queueService.LeaveQueueAsync(playerId, DefaultVariant);

        // Assert
        result1.Type.Should().Be(LeaveQueueResultType.LeftSuccessfully);
        result2.Type.Should().Be(LeaveQueueResultType.NotInQueue);
        
        // Verify TryLeaveQueueAsync was called only once (first call)
        _queueStoreMock.Verify(
            x => x.TryLeaveQueueAsync(DefaultVariant, playerId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetStatusAsync_WhenMatchIsCompleted_ReturnsIdleIfNotQueued()
    {
        // Arrange
        var playerId = Guid.NewGuid();
        var match = new Match
        {
            MatchId = Guid.NewGuid(),
            BattleId = Guid.NewGuid(),
            PlayerAId = playerId,
            PlayerBId = Guid.NewGuid(),
            Variant = DefaultVariant,
            State = MatchState.Completed, // Completed match is not active
            CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            UpdatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
        };

        _matchRepositoryMock
            .Setup(x => x.GetLatestForPlayerAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);
        _queueStoreMock
            .Setup(x => x.IsQueuedAsync(DefaultVariant, playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _queueService.GetStatusAsync(playerId);

        // Assert
        result.Should().BeNull(); // Idle - match is completed, not queued
    }
}

