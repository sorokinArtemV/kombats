using System.Diagnostics;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Combats.Infrastructure.Messaging.Filters;

public class ConsumeLoggingFilter<T> : IFilter<ConsumeContext<T>> where T : class
{
    private readonly ILogger<ConsumeLoggingFilter<T>> _logger;

    public ConsumeLoggingFilter(ILogger<ConsumeLoggingFilter<T>> logger)
    {
        _logger = logger;
    }

    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        var stopwatch = Stopwatch.StartNew();
        var messageId = context.MessageId?.ToString() ?? "unknown";
        var correlationId = context.CorrelationId?.ToString() ?? "none";
        var conversationId = context.ConversationId?.ToString() ?? "none";
        var causationId = context.Headers?.Get<string>("CausationId") ?? "none";
        var messageType = typeof(T).Name;
        var consumer = context.ReceiveContext?.InputAddress?.AbsolutePath?.Split('/').LastOrDefault() ?? "unknown";
        var endpoint = context.ReceiveContext?.InputAddress?.ToString() ?? "unknown";

        _logger.LogInformation(
            "Consuming message {MessageId} of type {MessageType} on endpoint {Endpoint} by consumer {Consumer}. CorrelationId: {CorrelationId}, ConversationId: {ConversationId}, CausationId: {CausationId}",
            messageId, messageType, endpoint, consumer, correlationId, conversationId, causationId);

        try
        {
            await next.Send(context);
            stopwatch.Stop();

            _logger.LogInformation(
                "Successfully consumed message {MessageId} of type {MessageType} on endpoint {Endpoint} by consumer {Consumer}. Duration: {DurationMs}ms",
                messageId, messageType, endpoint, consumer, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.LogError(
                ex,
                "Failed to consume message {MessageId} of type {MessageType} on endpoint {Endpoint} by consumer {Consumer}. Duration: {DurationMs}ms",
                messageId, messageType, endpoint, consumer, stopwatch.ElapsedMilliseconds);

            throw;
        }
    }

    public void Probe(ProbeContext context)
    {
        context.CreateFilterScope("consumeLogging");
    }
}

