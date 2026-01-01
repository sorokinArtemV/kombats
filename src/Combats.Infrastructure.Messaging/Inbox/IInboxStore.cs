namespace Combats.Infrastructure.Messaging.Inbox;

public interface IInboxStore
{
    /// <summary>
    /// Attempts to begin processing a message. Returns true if the message can be processed (new or retryable).
    /// Returns false if the message is already processed.
    /// Throws InboxProcessingException if the message is currently being processed by another instance.
    /// </summary>
    Task<InboxProcessingResult> TryBeginProcessingAsync(
        Guid messageId,
        string consumerId,
        DateTime expiresAt,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks a message as processed. Must be called after successful handler execution.
    /// </summary>
    Task MarkProcessedAsync(
        Guid messageId,
        string consumerId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Releases a message from processing state (e.g., on handler failure).
    /// Allows retry/redelivery to re-attempt processing.
    /// </summary>
    Task ReleaseProcessingAsync(
        Guid messageId,
        string consumerId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes expired inbox messages (for retention cleanup).
    /// </summary>
    Task<int> DeleteExpiredAsync(DateTime cutoff, CancellationToken cancellationToken);
}

public enum InboxProcessingResult
{
    /// <summary>
    /// Message is new and can be processed.
    /// </summary>
    CanProcess,

    /// <summary>
    /// Message is already processed. Handler should not be invoked.
    /// </summary>
    AlreadyProcessed,

    /// <summary>
    /// Message is currently being processed. Should throw retryable exception.
    /// </summary>
    CurrentlyProcessing
}

