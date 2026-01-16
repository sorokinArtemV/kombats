namespace Kombats.Battle.Api.Workers;

/// <summary>
/// Configuration options for TurnDeadlineWorker.
/// </summary>
public class TurnDeadlineWorkerOptions
{
    /// <summary>
    /// Maximum number of battles to claim and process per iteration.
    /// </summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// TTL for claim lease. Battles claimed by this worker are locked for this duration.
    /// If worker crashes, lease expires and battles become available for other workers.
    /// Default: 12 seconds (safe value to allow processing time).
    /// </summary>
    public TimeSpan ClaimLeaseTtl { get; set; } = TimeSpan.FromSeconds(12);

    /// <summary>
    /// Minimum delay (ms) when no battles are claimed (idle state).
    /// </summary>
    public int IdleDelayMinMs { get; set; } = 200;

    /// <summary>
    /// Maximum delay (ms) when no battles are claimed (idle state).
    /// </summary>
    public int IdleDelayMaxMs { get; set; } = 1000;

    /// <summary>
    /// Delay (ms) when backlog exists (battles were claimed and processed).
    /// Small delay to drain backlog efficiently.
    /// </summary>
    public int BacklogDelayMs { get; set; } = 30;

    /// <summary>
    /// Maximum number of backoff steps for exponential backoff when idle.
    /// Formula: delay = IdleDelayMinMs * 2^min(consecutiveEmptyIterations - 1, MaxBackoffSteps)
    /// </summary>
    public int MaxBackoffSteps { get; set; } = 3;

    /// <summary>
    /// Threshold for transient errors per iteration to log warning summary.
    /// If transientErrorCount >= this value, a warning summary is logged.
    /// </summary>
    public int TransientErrorWarnThreshold { get; set; } = 5;

    /// <summary>
    /// Delay (ms) when an iteration-level error occurs.
    /// Default: same as IdleDelayMinMs to preserve previous behavior.
    /// </summary>
    public int ErrorDelayMs { get; set; } = 200;
}

