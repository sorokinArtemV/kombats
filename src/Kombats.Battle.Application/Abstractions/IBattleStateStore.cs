using System;
using Kombats.Battle.Domain.Model;
using Kombats.Battle.Application.ReadModels;

namespace Kombats.Battle.Application.Abstractions;

/// <summary>
/// Port interface for battle state persistence.
/// Application defines what it needs; Infrastructure provides implementation.
/// Works with domain models for writes and read models for queries.
/// </summary>
public interface IBattleStateStore
{
    Task<bool> TryInitializeBattleAsync(Guid battleId, BattleDomainState initialState, CancellationToken cancellationToken = default);
    Task<BattleSnapshot?> GetStateAsync(Guid battleId, CancellationToken cancellationToken = default);
    Task<bool> TryOpenTurnAsync(Guid battleId, int turnIndex, DateTime deadlineUtc, CancellationToken cancellationToken = default);
    Task<bool> TryMarkTurnResolvingAsync(Guid battleId, int turnIndex, CancellationToken cancellationToken = default);
    Task<bool> MarkTurnResolvedAndOpenNextAsync(
        Guid battleId, 
        int currentTurnIndex, 
        int nextTurnIndex, 
        DateTime nextDeadlineUtc, 
        int noActionStreak,
        int playerAHp,
        int playerBHp,
        CancellationToken cancellationToken = default);
    Task<EndBattleCommitResult> EndBattleAndMarkResolvedAsync(
        Guid battleId, 
        int turnIndex, 
        int noActionStreak,
        int playerAHp,
        int playerBHp,
        CancellationToken cancellationToken = default);
    Task<List<Guid>> GetActiveBattlesAsync(CancellationToken cancellationToken = default);
    
    // Deadline index methods (Redis ZSET)
    /// <summary>
    /// Production code must use ClaimDueBattlesAsync for deadline polling.
    /// TurnDeadlineWorker uses a tick loop with ClaimDueBattlesAsync and adaptive backoff.
    /// </summary>
    /// <remarks>
    /// Do not use in runtime flow; deadlines are managed by state transitions + claim.
    /// Kept for diagnostics/admin tooling only.
    /// </remarks>
    [Obsolete("Do not use in runtime flow; deadlines are managed by state transitions + claim. Use ClaimDueBattlesAsync for production deadline polling.")]
    Task AddBattleDeadlineAsync(Guid battleId, DateTime deadlineUtc, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Production code must use ClaimDueBattlesAsync for deadline polling.
    /// TurnDeadlineWorker uses a tick loop with ClaimDueBattlesAsync and adaptive backoff.
    /// </summary>
    /// <remarks>
    /// Do not use in runtime flow; deadlines are managed by state transitions + claim.
    /// Kept for diagnostics/admin tooling only.
    /// </remarks>
    [Obsolete("Do not use in runtime flow; deadlines are managed by state transitions + claim. Use ClaimDueBattlesAsync for production deadline polling.")]
    Task RemoveBattleDeadlineAsync(Guid battleId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Legacy method - not used by production worker.
    /// Production code must use ClaimDueBattlesAsync for deadline polling.
    /// TurnDeadlineWorker uses a tick loop with ClaimDueBattlesAsync and adaptive backoff.
    /// </summary>
    [Obsolete("Legacy method - not used by production worker. Use ClaimDueBattlesAsync for production deadline polling.")]
    Task<List<Guid>> GetDueBattlesAsync(DateTime nowUtc, int limit, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets the next upcoming deadline from the deadlines ZSET.
    /// Returns null if there are no active deadlines.
    /// 
    /// WARNING: Unsafe due to double precision loss when Redis returns scores.
    /// Redis ZSET scores are stored as doubles, and DateTime ticks (long) may lose precision when converted.
    /// Production code must use ClaimDueBattlesAsync tick loop instead of peek/sleep patterns.
    /// </summary>
    [Obsolete("Unsafe due to double precision; do not use. Use ClaimDueBattlesAsync tick loop in TurnDeadlineWorker.")]
    Task<DateTime?> GetNextDeadlineUtcAsync(CancellationToken cancellationToken = default);
    /// <summary>
    /// Claims due battles from the deadlines ZSET atomically using Redis locks.
    /// For each due battle, attempts to acquire a lease lock for the specific battle turn.
    /// Only battles where the lock is successfully acquired are returned and removed from the ZSET.
    /// If battle state is missing, the battle is removed from ZSET and skipped.
    /// This ensures only one worker processes a given battle turn, preventing duplicate resolutions.
    /// </summary>
    /// <param name="nowUtc">Current UTC time to determine which battles are due</param>
    /// <param name="limit">Maximum number of battles to claim in one call</param>
    /// <param name="leaseTtl">Time-to-live for the claim lock (should be long enough to complete resolution)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of claimed battles with their turn indexes</returns>
    Task<IReadOnlyList<ClaimedBattleDue>> ClaimDueBattlesAsync(DateTime nowUtc, int limit, TimeSpan leaseTtl, CancellationToken cancellationToken = default);
    Task<ActionStoreResult> StoreActionAsync(Guid battleId, int turnIndex, Guid playerId, string actionPayload, CancellationToken cancellationToken = default);
    Task<(string? PlayerAAction, string? PlayerBAction)> GetActionsAsync(Guid battleId, int turnIndex, Guid playerAId, Guid playerBId, CancellationToken cancellationToken = default);
}




