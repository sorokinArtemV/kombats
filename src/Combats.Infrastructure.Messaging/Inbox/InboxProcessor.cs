using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Combats.Infrastructure.Messaging.Options;

namespace Combats.Infrastructure.Messaging.Inbox;

public class InboxProcessor : IInboxProcessor
{
    private readonly IInboxStore _inboxStore;
    private readonly IOptions<MessagingOptions> _options;
    private readonly ILogger<InboxProcessor> _logger;

    public InboxProcessor(
        IInboxStore inboxStore,
        IOptions<MessagingOptions> options,
        ILogger<InboxProcessor> logger)
    {
        _inboxStore = inboxStore;
        _options = options;
        _logger = logger;
    }

    public async Task ProcessAsync<T>(
        ConsumeContext<T> context,
        string consumerId,
        Func<ConsumeContext<T>, Task> handler,
        CancellationToken cancellationToken)
        where T : class
    {
        if (!context.MessageId.HasValue)
        {
            throw new InvalidOperationException("MessageId is required for inbox processing");
        }

        var messageId = context.MessageId.Value;
        var expiresAt = DateTime.UtcNow.AddDays(_options.Value.Inbox.RetentionDays);

        // Attempt to begin processing
        var result = await _inboxStore.TryBeginProcessingAsync(
            messageId,
            consumerId,
            expiresAt,
            cancellationToken);

        switch (result)
        {
            case InboxProcessingResult.AlreadyProcessed:
                _logger.LogInformation(
                    "Message {MessageId} already processed by consumer {ConsumerId}, skipping handler",
                    messageId, consumerId);
                // ACK without invoking handler
                return;

            case InboxProcessingResult.CurrentlyProcessing:
                _logger.LogWarning(
                    "Message {MessageId} is currently being processed by consumer {ConsumerId}, throwing retryable exception",
                    messageId, consumerId);
                // Throw retryable exception to trigger MassTransit retry/redelivery
                throw new InboxProcessingException(messageId, consumerId);

            case InboxProcessingResult.CanProcess:
                // Proceed with handler execution
                try
                {
                    await handler(context);

                    // Handler succeeded - mark as processed
                    await _inboxStore.MarkProcessedAsync(
                        messageId,
                        consumerId,
                        cancellationToken);

                    _logger.LogDebug(
                        "Message {MessageId} processed successfully by consumer {ConsumerId}",
                        messageId, consumerId);
                }
                catch (Exception ex)
                {
                    // Handler failed - release from processing to allow retry
                    await _inboxStore.ReleaseProcessingAsync(
                        messageId,
                        consumerId,
                        cancellationToken);

                    _logger.LogWarning(
                        ex,
                        "Message {MessageId} processing failed by consumer {ConsumerId}, released for retry",
                        messageId, consumerId);

                    // Rethrow to trigger MassTransit retry/redelivery
                    throw;
                }
                break;

            default:
                throw new InvalidOperationException($"Unknown inbox processing result: {result}");
        }
    }
}

