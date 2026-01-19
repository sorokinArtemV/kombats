using FluentAssertions;
using Kombats.Matchmaking.Application.Abstractions;
using Kombats.Matchmaking.Application.UseCases;
using Kombats.Matchmaking.Domain;
using Microsoft.Extensions.Logging;
using Moq;
using Match = Kombats.Matchmaking.Domain.Match;
using Xunit;

namespace Kombats.Matchmaking.Application.Tests;

public class MatchmakingServiceTests
{
    private readonly Mock<IMatchQueueStore> _queueStoreMock;
    private readonly Mock<IMatchRepository> _matchRepositoryMock;
    private readonly Mock<IOutboxWriter> _outboxWriterMock;
    private readonly Mock<ITransactionManager> _transactionManagerMock;
    private readonly Mock<ILogger<MatchmakingService>> _loggerMock;
    private readonly Mock<ITransactionHandle> _transactionHandleMock;
    private readonly MatchmakingService _matchmakingService;
    private const string DefaultVariant = "default";

    public MatchmakingServiceTests()
    {
        _queueStoreMock = new Mock<IMatchQueueStore>();
        _matchRepositoryMock = new Mock<IMatchRepository>();
        _outboxWriterMock = new Mock<IOutboxWriter>();
        _transactionManagerMock = new Mock<ITransactionManager>();
        _loggerMock = new Mock<ILogger<MatchmakingService>>();
        _transactionHandleMock = new Mock<ITransactionHandle>();

        _transactionManagerMock
            .Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_transactionHandleMock.Object);

        _matchmakingService = new MatchmakingService(
            _queueStoreMock.Object,
            _matchRepositoryMock.Object,
            _outboxWriterMock.Object,
            _transactionManagerMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task MatchmakingTickAsync_WhenNoPairAvailable_ReturnsNoMatch()
    {
        // Arrange
        _queueStoreMock
            .Setup(x => x.TryPopPairAsync(DefaultVariant, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default((Guid PlayerAId, Guid PlayerBId)?));

        // Act
        var result = await _matchmakingService.MatchmakingTickAsync(DefaultVariant);

        // Assert
        result.Type.Should().Be(MatchCreatedResultType.NoMatch);
        _matchRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<Match>(), It.IsAny<CancellationToken>()), Times.Never);
        _outboxWriterMock.Verify(x => x.EnqueueAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()), Times.Never);
        _transactionManagerMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MatchmakingTickAsync_WhenPairAvailable_CallsSaveChangesOnce()
    {
        // Arrange
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();

        _queueStoreMock
            .Setup(x => x.TryPopPairAsync(DefaultVariant, It.IsAny<CancellationToken>()))
            .ReturnsAsync((playerAId, playerBId));

        // Act
        var result = await _matchmakingService.MatchmakingTickAsync(DefaultVariant);

        // Assert
        result.Type.Should().Be(MatchCreatedResultType.MatchCreated);
        
        // Verify repository and outbox were called (to add entities)
        _matchRepositoryMock.Verify(
            x => x.InsertAsync(It.IsAny<Match>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _outboxWriterMock.Verify(
            x => x.EnqueueAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // CRITICAL: SaveChangesAsync should be called exactly ONCE
        _transactionManagerMock.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once,
            "SaveChangesAsync must be called exactly once after both entities are added");

        // Verify transaction was committed
        _transactionHandleMock.Verify(
            x => x.CommitAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task MatchmakingTickAsync_WhenSaveChangesFails_RollsBackTransaction()
    {
        // Arrange
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();

        _queueStoreMock
            .Setup(x => x.TryPopPairAsync(DefaultVariant, It.IsAny<CancellationToken>()))
            .ReturnsAsync((playerAId, playerBId));

        _transactionManagerMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database error"));

        // Act
        Func<Task> act = async () => await _matchmakingService.MatchmakingTickAsync(DefaultVariant);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();

        // Verify rollback was called
        _transactionHandleMock.Verify(
            x => x.RollbackAsync(It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify commit was NOT called
        _transactionHandleMock.Verify(
            x => x.CommitAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }
}

