namespace Combats.Battle.Application.Abstractions;

/// <summary>
/// Result of attempting to end a battle and mark it resolved.
/// Distinguishes between different outcomes for proper idempotency handling.
/// </summary>
public enum EndBattleCommitResult
{
    /// <summary>
    /// Battle was already in Ended phase (idempotent case).
    /// </summary>
    AlreadyEnded = 2,

    /// <summary>
    /// Battle transitioned to Ended in this call.
    /// </summary>
    EndedNow = 1,

    /// <summary>
    /// Battle could not be ended (wrong phase/turn, not committed).
    /// </summary>
    NotCommitted = 0
}


