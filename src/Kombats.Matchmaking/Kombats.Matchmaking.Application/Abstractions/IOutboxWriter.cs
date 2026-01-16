namespace Kombats.Matchmaking.Application.Abstractions;

/// <summary>
/// Port for writing outbox messages (transactional outbox pattern).
/// Ensures messages are persisted atomically with business data in the same transaction.
/// </summary>
public interface IOutboxWriter
{
    /// <summary>
    /// Enqueues an outbox message to be published later by OutboxDispatcher.
    /// Must be called within the same transaction as business data changes.
    /// </summary>
    Task EnqueueAsync(OutboxMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// Outbox message model for application layer.
/// </summary>
public class OutboxMessage
{
    public required Guid Id { get; init; }
    public required DateTime OccurredAtUtc { get; init; }
    public required string Type { get; init; }
    public required string Payload { get; init; }
    public Guid? CorrelationId { get; init; }
}

