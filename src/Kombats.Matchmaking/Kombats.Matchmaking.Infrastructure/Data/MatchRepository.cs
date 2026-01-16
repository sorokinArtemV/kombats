using Kombats.Matchmaking.Application.Abstractions;
using Kombats.Matchmaking.Domain;
using Kombats.Matchmaking.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kombats.Matchmaking.Infrastructure.Data;

/// <summary>
/// Infrastructure implementation of IMatchRepository using EF Core.
/// </summary>
public class MatchRepository : IMatchRepository
{
    private readonly MatchmakingDbContext _dbContext;
    private readonly ILogger<MatchRepository> _logger;

    public MatchRepository(
        MatchmakingDbContext dbContext,
        ILogger<MatchRepository> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<Match?> GetLatestForPlayerAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var entity = await _dbContext.Matches
                .Where(m => m.PlayerAId == playerId || m.PlayerBId == playerId)
                .OrderByDescending(m => m.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            return entity == null ? null : ToDomain(entity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error in GetLatestForPlayerAsync for PlayerId: {PlayerId}",
                playerId);
            throw;
        }
    }

    public async Task<Match?> GetByMatchIdAsync(Guid matchId, CancellationToken cancellationToken = default)
    {
        try
        {
            var entity = await _dbContext.Matches
                .FirstOrDefaultAsync(m => m.MatchId == matchId, cancellationToken);

            return entity == null ? null : ToDomain(entity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error in GetByMatchIdAsync for MatchId: {MatchId}",
                matchId);
            throw;
        }
    }

    public async Task InsertAsync(Match match, CancellationToken cancellationToken = default)
    {
        try
        {
            var entity = ToEntity(match);
            _dbContext.Matches.Add(entity);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Inserted match: MatchId={MatchId}, BattleId={BattleId}, PlayerA={PlayerAId}, PlayerB={PlayerBId}",
                match.MatchId, match.BattleId, match.PlayerAId, match.PlayerBId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error in InsertAsync for MatchId: {MatchId}",
                match.MatchId);
            throw;
        }
    }

    public async Task UpdateStateAsync(Guid matchId, MatchState newState, DateTime updatedAtUtc, CancellationToken cancellationToken = default)
    {
        try
        {
            var entity = await _dbContext.Matches
                .FirstOrDefaultAsync(m => m.MatchId == matchId, cancellationToken);

            if (entity == null)
            {
                _logger.LogWarning(
                    "Match not found for UpdateStateAsync: MatchId={MatchId}",
                    matchId);
                throw new InvalidOperationException($"Match not found: {matchId}");
            }

            entity.State = (int)newState;
            entity.UpdatedAtUtc = new DateTimeOffset(updatedAtUtc, TimeSpan.Zero);

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Updated match state: MatchId={MatchId}, NewState={NewState}",
                matchId, newState);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error in UpdateStateAsync for MatchId: {MatchId}, NewState: {NewState}",
                matchId, newState);
            throw;
        }
    }

    public async Task<bool> TryUpdateStateAsync(
        Guid matchId,
        MatchState expectedState,
        MatchState newState,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // CAS update: only update if current state matches expected state
            var affectedRows = await _dbContext.Matches
                .Where(m => m.MatchId == matchId && m.State == (int)expectedState)
                .ExecuteUpdateAsync(
                    setter => setter
                        .SetProperty(m => m.State, (int)newState)
                        .SetProperty(m => m.UpdatedAtUtc, new DateTimeOffset(updatedAtUtc, TimeSpan.Zero)),
                    cancellationToken);

            var success = affectedRows > 0;

            if (success)
            {
                _logger.LogInformation(
                    "CAS update succeeded: MatchId={MatchId}, ExpectedState={ExpectedState}, NewState={NewState}",
                    matchId, expectedState, newState);
            }
            else
            {
                _logger.LogWarning(
                    "CAS update failed (state mismatch or match not found): MatchId={MatchId}, ExpectedState={ExpectedState}, NewState={NewState}",
                    matchId, expectedState, newState);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error in TryUpdateStateAsync for MatchId: {MatchId}, ExpectedState: {ExpectedState}, NewState: {NewState}",
                matchId, expectedState, newState);
            throw;
        }
    }

    private static Match ToDomain(MatchEntity entity)
    {
        return new Match
        {
            MatchId = entity.MatchId,
            BattleId = entity.BattleId,
            PlayerAId = entity.PlayerAId,
            PlayerBId = entity.PlayerBId,
            Variant = entity.Variant,
            State = (MatchState)entity.State,
            CreatedAtUtc = entity.CreatedAtUtc,
            UpdatedAtUtc = entity.UpdatedAtUtc
        };
    }

    private static MatchEntity ToEntity(Match match)
    {
        // Ensure UTC: DateTimeOffset.UtcNow already has offset=0, but normalize to be safe
        var createdAtUtc = match.CreatedAtUtc.Offset == TimeSpan.Zero
            ? match.CreatedAtUtc
            : new DateTimeOffset(match.CreatedAtUtc.UtcDateTime, TimeSpan.Zero);
        
        var updatedAtUtc = match.UpdatedAtUtc.Offset == TimeSpan.Zero
            ? match.UpdatedAtUtc
            : new DateTimeOffset(match.UpdatedAtUtc.UtcDateTime, TimeSpan.Zero);

        return new MatchEntity
        {
            MatchId = match.MatchId,
            BattleId = match.BattleId,
            PlayerAId = match.PlayerAId,
            PlayerBId = match.PlayerBId,
            Variant = match.Variant,
            State = (int)match.State,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = updatedAtUtc
        };
    }
}

