namespace Combats.Infrastructure.Messaging.Inbox;

/// <summary>
/// Exception thrown when a message is currently being processed and cannot be handled.
/// This is a retryable exception that will trigger MassTransit retry/redelivery.
/// </summary>
public class InboxProcessingException : InvalidOperationException
{
    public InboxProcessingException(Guid messageId, string consumerId)
        : base($"Message {messageId} is currently being processed by consumer {consumerId}. This may indicate a concurrent processing attempt or a previous crash. Retrying...")
    {
        MessageId = messageId;
        ConsumerId = consumerId;
    }

    public Guid MessageId { get; }
    public string ConsumerId { get; }
}

