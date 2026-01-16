using Kombats.Contracts.Battle;
using Kombats.Matchmaking.Application.Abstractions;
using Kombats.Matchmaking.Domain;
using Kombats.Matchmaking.Infrastructure.Data.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kombats.Matchmaking.Infrastructure.Data;

/// <summary>
/// Infrastructure implementation of IMatchRepository using EF Core.
/// </summary>
public class MatchRepository : IMatchRepository
{
    private readonly MatchmakingDbContext _dbContext;
    private readonly ISendEndpointProvider _sendEndpointProvider;
    private readonly ILogger<MatchRepository> _logger;

    public MatchRepository(
        MatchmakingDbContext dbContext,
        ISendEndpointProvider sendEndpointProvider,
        ILogger<MatchRepository> logger)
    {
        _dbContext = dbContext;
        _sendEndpointProvider = sendEndpointProvider;
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

    public async Task UpdateStateAsync(Guid matchId, MatchState newState, DateTimeOffset updatedAtUtc, CancellationToken cancellationToken = default)
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
            entity.UpdatedAtUtc = updatedAtUtc.DateTime;

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

    public async Task CreateMatchAndSendCommandAsync(
        Match match,
        CreateBattle createBattleCommand,
        MatchState targetState,
        CancellationToken cancellationToken = default)
    {
        // In ONE DB transaction:
        // 1) Insert Match (state = Created)
        // 2) Send CreateBattle command (will be stored in outbox if using ConsumeContext, but we're in a background service)
        // 3) Update Match state -> BattleCreateRequested
        using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // 1) Insert Match (state = Created)
            var entity = ToEntity(match);
            _dbContext.Matches.Add(entity);

            // 2) Send CreateBattle command
            // Note: ISendEndpointProvider.Send() doesn't automatically use outbox from background services
            // For true transactional outbox, we'd need to use IOutboxMessageStore directly
            // For now, we send the command and rely on transaction rollback if it fails
            var endpoint = await _sendEndpointProvider.GetSendEndpoint(new Uri("queue:battle.create-battle"));
            await endpoint.Send(createBattleCommand, cancellationToken);

            // 3) Update Match state -> BattleCreateRequested
            entity.State = (int)targetState;
            entity.UpdatedAtUtc = DateTimeOffset.UtcNow.DateTime;

            // Save all changes atomically
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Created match and sent CreateBattle command: MatchId={MatchId}, BattleId={BattleId}, PlayerA={PlayerAId}, PlayerB={PlayerBId}",
                match.MatchId, match.BattleId, match.PlayerAId, match.PlayerBId);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex,
                "Failed to create match and send CreateBattle command: MatchId={MatchId}, BattleId={BattleId}",
                match.MatchId, match.BattleId);
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
            CreatedAtUtc = new DateTimeOffset(DateTime.SpecifyKind(entity.CreatedAtUtc, DateTimeKind.Utc)),
            UpdatedAtUtc = new DateTimeOffset(DateTime.SpecifyKind(entity.UpdatedAtUtc, DateTimeKind.Utc))
        };
    }

    private static MatchEntity ToEntity(Match match)
    {
        return new MatchEntity
        {
            MatchId = match.MatchId,
            BattleId = match.BattleId,
            PlayerAId = match.PlayerAId,
            PlayerBId = match.PlayerBId,
            Variant = match.Variant,
            State = (int)match.State,
            CreatedAtUtc = match.CreatedAtUtc.DateTime,
            UpdatedAtUtc = match.UpdatedAtUtc.DateTime
        };
    }
}

