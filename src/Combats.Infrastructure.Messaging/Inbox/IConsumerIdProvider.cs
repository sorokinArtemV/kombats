using MassTransit;

namespace Combats.Infrastructure.Messaging.Inbox;

/// <summary>
/// Provides stable consumer identifiers for inbox processing.
/// </summary>
public interface IConsumerIdProvider
{
    /// <summary>
    /// Gets the consumer identifier for a given message context.
    /// The identifier must be stable and not depend on transport addresses.
    /// </summary>
    string GetConsumerId<T>(ConsumeContext<T> context) where T : class;
}

