using MassTransit;
using MassTransit.Transports;

namespace Combats.Infrastructure.Messaging.Inbox;

/// <summary>
/// Default consumer ID provider that uses the endpoint name as a stable identifier.
/// The endpoint name in MassTransit is typically derived from the consumer type name,
/// making it a stable identifier for inbox processing.
/// Services can register a custom implementation to provide explicit consumer IDs.
/// </summary>
public class ConsumerIdProvider : IConsumerIdProvider
{
    public string GetConsumerId<T>(ConsumeContext<T> context) where T : class
    {
        // Use endpoint name as consumer identifier
        // Endpoint names in MassTransit are stable and typically based on consumer type
        var endpointName = context.ReceiveContext?.InputAddress?.GetEndpointName();
        
        if (!string.IsNullOrWhiteSpace(endpointName))
        {
            return endpointName;
        }

        // Fallback: use message type name
        // This is less ideal but provides a stable identifier
        return typeof(T).Name;
    }
}

