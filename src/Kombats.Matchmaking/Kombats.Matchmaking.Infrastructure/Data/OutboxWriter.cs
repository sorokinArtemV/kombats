using Kombats.Matchmaking.Application.Abstractions;
using Kombats.Matchmaking.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Kombats.Matchmaking.Infrastructure.Data;

/// <summary>
/// Infrastructure implementation of IOutboxWriter using EF Core.
/// Writes outbox messages to the database in the same transaction as business data.
/// </summary>
public class OutboxWriter : IOutboxWriter
{
    private readonly MatchmakingDbContext _dbContext;
    private readonly ILogger<OutboxWriter> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public OutboxWriter(
        MatchmakingDbContext dbContext,
        ILogger<OutboxWriter> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task EnqueueAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            var entity = new OutboxMessageEntity
            {
                Id = message.Id,
                OccurredAtUtc = message.OccurredAtUtc,
                Type = message.Type,
                Payload = message.Payload,
                CorrelationId = message.CorrelationId,
                Status = OutboxMessageStatus.Pending
            };

            await _dbContext.OutboxMessages.AddAsync(entity, cancellationToken);
            // Note: SaveChangesAsync is NOT called here - it should be called
            // in the same transaction as business data changes (e.g., in MatchmakingService)
            
            _logger.LogDebug(
                "Enqueued outbox message: Id={MessageId}, Type={MessageType}",
                message.Id, message.Type);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error enqueueing outbox message: Id={MessageId}, Type={MessageType}",
                message.Id, message.Type);
            throw;
        }
    }
}

