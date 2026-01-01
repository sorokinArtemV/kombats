using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Combats.Infrastructure.Messaging.Inbox;

public class InboxStore<TDbContext> : IInboxStore
    where TDbContext : DbContext
{
    private readonly TDbContext _dbContext;
    private readonly ILogger<InboxStore<TDbContext>> _logger;

    public InboxStore(TDbContext dbContext, ILogger<InboxStore<TDbContext>> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<InboxProcessingResult> TryBeginProcessingAsync(
        Guid messageId,
        string consumerId,
        DateTime expiresAt,
        CancellationToken cancellationToken)
    {
        // Check if message already exists
        var existing = await _dbContext.Set<InboxMessage>()
            .FirstOrDefaultAsync(
                x => x.MessageId == messageId && x.ConsumerId == consumerId,
                cancellationToken);

        if (existing != null)
        {
            if (existing.Status == InboxMessageStatus.Processed)
            {
                _logger.LogDebug(
                    "Message {MessageId} already processed by consumer {ConsumerId}",
                    messageId, consumerId);
                return InboxProcessingResult.AlreadyProcessed;
            }

            if (existing.Status == InboxMessageStatus.Processing)
            {
                _logger.LogWarning(
                    "Message {MessageId} is currently being processed by consumer {ConsumerId}",
                    messageId, consumerId);
                return InboxProcessingResult.CurrentlyProcessing;
            }

            // Status is Received - update to Processing
            existing.Status = InboxMessageStatus.Processing;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return InboxProcessingResult.CanProcess;
        }

        // Message is new - insert as Processing
        try
        {
            var inboxMessage = new InboxMessage
            {
                MessageId = messageId,
                ConsumerId = consumerId,
                Status = InboxMessageStatus.Processing,
                ReceivedAt = DateTime.UtcNow,
                ExpiresAt = expiresAt
            };

            _dbContext.Set<InboxMessage>().Add(inboxMessage);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogDebug(
                "Began processing message {MessageId} for consumer {ConsumerId}",
                messageId, consumerId);

            return InboxProcessingResult.CanProcess;
        }
        catch (DbUpdateException dbEx) when (IsUniqueViolation(dbEx))
        {
            // Concurrent insert - check current state
            var concurrent = await _dbContext.Set<InboxMessage>()
                .FirstOrDefaultAsync(
                    x => x.MessageId == messageId && x.ConsumerId == consumerId,
                    cancellationToken);

            if (concurrent == null)
            {
                // Race condition - retry
                throw new InvalidOperationException(
                    $"Concurrent insert detected for message {messageId}, consumer {consumerId}. Retrying...",
                    dbEx);
            }

            if (concurrent.Status == InboxMessageStatus.Processed)
            {
                return InboxProcessingResult.AlreadyProcessed;
            }

            if (concurrent.Status == InboxMessageStatus.Processing)
            {
                return InboxProcessingResult.CurrentlyProcessing;
            }

            // Should not reach here, but handle gracefully
            throw new InvalidOperationException(
                $"Unexpected inbox state for message {messageId}, consumer {consumerId}: {concurrent.Status}",
                dbEx);
        }
    }

    public async Task MarkProcessedAsync(
        Guid messageId,
        string consumerId,
        CancellationToken cancellationToken)
    {
        var inboxMessage = await _dbContext.Set<InboxMessage>()
            .FirstOrDefaultAsync(
                x => x.MessageId == messageId && x.ConsumerId == consumerId,
                cancellationToken);

        if (inboxMessage == null)
        {
            _logger.LogWarning(
                "Attempted to mark message {MessageId} as processed for consumer {ConsumerId}, but message not found",
                messageId, consumerId);
            return;
        }

        inboxMessage.Status = InboxMessageStatus.Processed;
        inboxMessage.ProcessedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogDebug(
            "Marked message {MessageId} as processed for consumer {ConsumerId}",
            messageId, consumerId);
    }

    public async Task ReleaseProcessingAsync(
        Guid messageId,
        string consumerId,
        CancellationToken cancellationToken)
    {
        var inboxMessage = await _dbContext.Set<InboxMessage>()
            .FirstOrDefaultAsync(
                x => x.MessageId == messageId && x.ConsumerId == consumerId,
                cancellationToken);

        if (inboxMessage == null)
        {
            _logger.LogWarning(
                "Attempted to release message {MessageId} from processing for consumer {ConsumerId}, but message not found",
                messageId, consumerId);
            return;
        }

        // Delete the row to allow retry/redelivery
        _dbContext.Set<InboxMessage>().Remove(inboxMessage);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogDebug(
            "Released message {MessageId} from processing for consumer {ConsumerId} (deleted for retry)",
            messageId, consumerId);
    }

    public async Task<int> DeleteExpiredAsync(DateTime cutoff, CancellationToken cancellationToken)
    {
        var deletedCount = await _dbContext.Set<InboxMessage>()
            .Where(x => x.ExpiresAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        return deletedCount;
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        // PostgreSQL unique violation error code
        return ex.InnerException?.Message?.Contains("23505") == true ||
               ex.InnerException?.Message?.Contains("duplicate key") == true ||
               ex.InnerException?.Message?.Contains("unique constraint") == true;
    }
}
