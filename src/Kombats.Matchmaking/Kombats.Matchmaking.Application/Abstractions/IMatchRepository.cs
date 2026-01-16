using Kombats.Contracts.Battle;
using Kombats.Matchmaking.Domain;

namespace Kombats.Matchmaking.Application.Abstractions;

/// <summary>
/// Port for match repository operations (Postgres source of truth).
/// </summary>
public interface IMatchRepository
{
    /// <summary>
    /// Gets the latest match for a player (by PlayerAId or PlayerBId).
    /// Returns null if no match found.
    /// </summary>
    Task<Match?> GetLatestForPlayerAsync(Guid playerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a match by MatchId.
    /// Returns null if not found.
    /// </summary>
    Task<Match?> GetByMatchIdAsync(Guid matchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a new match.
    /// </summary>
    Task InsertAsync(Match match, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the state of an existing match.
    /// </summary>
    Task UpdateStateAsync(Guid matchId, MatchState newState, DateTimeOffset updatedAtUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a match and sends CreateBattle command in a single transaction.
    /// This ensures atomicity: match insert, command send (outbox), and state update.
    /// </summary>
    Task CreateMatchAndSendCommandAsync(
        Match match,
        CreateBattle createBattleCommand,
        MatchState targetState,
        CancellationToken cancellationToken = default);
}

