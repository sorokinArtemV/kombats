using MassTransit;

namespace Combats.Infrastructure.Messaging.Inbox;

public interface IInboxProcessor
{
    /// <summary>
    /// Processes a message through the inbox with proper state management.
    /// </summary>
    Task ProcessAsync<T>(
        ConsumeContext<T> context,
        string consumerId,
        Func<ConsumeContext<T>, Task> handler,
        CancellationToken cancellationToken)
        where T : class;
}

