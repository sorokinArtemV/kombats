using System;
using Kombats.Battle.Domain.Model;
using Kombats.Battle.Application.ReadModels;
using Kombats.Battle.Application.UseCases.Turns;

namespace Kombats.Battle.Application.Abstractions;

/// <summary>
/// Port interface for battle state persistence.
/// Application defines what it needs; Infrastructure provides implementation.
/// Works with domain models for writes and read models for queries.
/// </summary>
public interface IBattleStateStore
{
    Task<bool> TryInitializeBattleAsync(
        Guid battleId, 
        BattleDomainState initialState,
        CancellationToken cancellationToken = default);

    Task<BattleSnapshot?> GetStateAsync(Guid battleId, CancellationToken cancellationToken = default);

    Task<bool> TryOpenTurnAsync(
        Guid battleId, 
        int turnIndex, 
        DateTimeOffset deadlineUtc, 
        CancellationToken cancellationToken = default);

    Task<bool> TryMarkTurnResolvingAsync(
        Guid battleId, 
        int turnIndex, 
        CancellationToken cancellationToken = default);

    Task<bool> MarkTurnResolvedAndOpenNextAsync(
        Guid battleId,
        int currentTurnIndex,
        int nextTurnIndex,
        DateTimeOffset nextDeadlineUtc,
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
    Task<IReadOnlyList<ClaimedBattleDue>> ClaimDueBattlesAsync(
        DateTimeOffset nowUtc, 
        int limit, 
        TimeSpan leaseTtl,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a canonical action command for a player in a specific turn.
    /// Uses first-write-wins semantics (SET NX in Redis).
    /// </summary>
    Task<ActionStoreResult> StoreActionAsync(
        Guid battleId, 
        int turnIndex, 
        Guid playerId, 
        PlayerActionCommand actionCommand,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores an action atomically and checks if both players have submitted actions.
    /// This is an optimization to avoid the extra GetActionsAsync roundtrip after storing.
    /// Uses first-write-wins semantics (SET NX in Redis) for each player's action.
    /// </summary>
    /// <param name="battleId">Battle identifier</param>
    /// <param name="turnIndex">Turn index</param>
    /// <param name="playerId">Player identifier</param>
    /// <param name="playerAId">Player A identifier (for role determination)</param>
    /// <param name="playerBId">Player B identifier (for role determination)</param>
    /// <param name="actionCommand">Canonical action command to store</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result containing store status and whether both players have submitted</returns>
    Task<ActionStoreAndCheckResult> StoreActionAndCheckBothSubmittedAsync(
        Guid battleId,
        int turnIndex,
        Guid playerId,
        Guid playerAId,
        Guid playerBId,
        PlayerActionCommand actionCommand,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves canonical action commands for both players in a specific turn.
    /// Returns null for a player if no action was stored.
    /// </summary>
    Task<(PlayerActionCommand? PlayerAAction, PlayerActionCommand? PlayerBAction)> GetActionsAsync(
        Guid battleId, 
        int turnIndex, 
        Guid playerAId,
        Guid playerBId, 
        CancellationToken cancellationToken = default);
}