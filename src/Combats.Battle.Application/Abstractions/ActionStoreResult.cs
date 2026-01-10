namespace Combats.Battle.Application.Abstractions;

/// <summary>
/// Result of attempting to store a player action.
/// </summary>
public enum ActionStoreResult
{
    /// <summary>
    /// Action was accepted and stored (first write wins).
    /// </summary>
    Accepted = 0,

    /// <summary>
    /// Action was already submitted for this turn (idempotent case).
    /// </summary>
    AlreadySubmitted = 1
}


