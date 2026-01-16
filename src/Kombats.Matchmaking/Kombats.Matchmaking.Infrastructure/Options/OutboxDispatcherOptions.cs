namespace Kombats.Matchmaking.Infrastructure.Options;

/// <summary>
/// Configuration options for OutboxDispatcher worker.
/// </summary>
public class OutboxDispatcherOptions
{
    public const string SectionName = "OutboxDispatcher";

    /// <summary>
    /// Interval between dispatcher ticks (milliseconds).
    /// Default: 5000ms (5 seconds).
    /// </summary>
    public int DispatchIntervalMs { get; set; } = 5000;

    /// <summary>
    /// Maximum number of pending messages to process in a single batch.
    /// Default: 50.
    /// </summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// Maximum number of retry attempts for a failed message.
    /// Default: 3.
    /// </summary>
    public int MaxRetryCount { get; set; } = 3;

    /// <summary>
    /// Base delay for exponential backoff retries (milliseconds).
    /// Default: 1000ms (1 second).
    /// </summary>
    public int RetryBaseDelayMs { get; set; } = 1000;

    /// <summary>
    /// Queue name for CreateBattle command.
    /// Default: "queue:battle.create-battle"
    /// </summary>
    public string CreateBattleQueueName { get; set; } = "queue:battle.create-battle";
}


