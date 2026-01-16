namespace Kombats.Matchmaking.Infrastructure.Entities;

/// <summary>
/// EF Core entity for outbox message storage (transactional outbox pattern).
/// Ensures messages are persisted atomically with business data.
/// </summary>
public class OutboxMessageEntity
{
    public Guid Id { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public Guid? CorrelationId { get; set; }
    public OutboxMessageStatus Status { get; set; }
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    public DateTime? LastAttemptAtUtc { get; set; }
}

/// <summary>
/// Outbox message status enumeration.
/// </summary>
public enum OutboxMessageStatus
{
    Pending = 0,
    Published = 1,
    Failed = 2
}


