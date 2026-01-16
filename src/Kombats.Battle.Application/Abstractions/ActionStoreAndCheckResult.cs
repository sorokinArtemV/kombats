namespace Kombats.Battle.Application.Abstractions;

/// <summary>
/// Result of atomically storing an action and checking if both players have submitted.
/// </summary>
public sealed record ActionStoreAndCheckResult
{
    /// <summary>
    /// Whether the action was accepted and stored (first write wins).
    /// </summary>
    public ActionStoreResult StoreResult { get; init; }

    /// <summary>
    /// Whether both players have submitted actions for this turn.
    /// </summary>
    public bool BothSubmitted { get; init; }

    /// <summary>
    /// Whether the action was stored in this call (true) or already existed (false).
    /// </summary>
    public bool WasStored { get; init; }
}

