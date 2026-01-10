namespace Combats.Infrastructure.Messaging.Options;

public class MessagingOptions
{
    public const string SectionName = "Messaging";

    public RabbitMqOptions RabbitMq { get; set; } = new();
    public TransportOptions Transport { get; set; } = new();
    public TopologyOptions Topology { get; set; } = new();
    public RetryOptions Retry { get; set; } = new();
    public RedeliveryOptions Redelivery { get; set; } = new();
    public OutboxOptions Outbox { get; set; } = new();
    public SchedulerOptions Scheduler { get; set; } = new();
}

public class RabbitMqOptions
{
    public string Host { get; set; } = string.Empty;
    public string VirtualHost { get; set; } = "/";
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool UseTls { get; set; } = false;
    public int HeartbeatSeconds { get; set; } = 30;
}

public class TransportOptions
{
    public ushort PrefetchCount { get; set; } = 32;
    public int ConcurrentMessageLimit { get; set; } = 8;
}

public class TopologyOptions
{
    public string EndpointPrefix { get; set; } = string.Empty;
    public string EntityNamePrefix { get; set; } = "combats";
    public bool UseKebabCase { get; set; } = true;
}

public class RetryOptions
{
    public string Mode { get; set; } = "Exponential";
    public int ExponentialCount { get; set; } = 5;
    public int ExponentialMinMs { get; set; } = 200;
    public int ExponentialMaxMs { get; set; } = 5000;
    public int ExponentialDeltaMs { get; set; } = 200;
}

public class RedeliveryOptions
{
    public bool Enabled { get; set; } = true;
    public int[] IntervalsSeconds { get; set; } = [30, 120, 600];
}

public class OutboxOptions
{
    public bool Enabled { get; set; } = true;
    public int QueryDelaySeconds { get; set; } = 1;
    public int DeliveryLimit { get; set; } = 500;
}

public class SchedulerOptions
{
    public bool Enabled { get; set; } = true; // Enable delayed message scheduler by default
}



