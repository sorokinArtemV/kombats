using Kombats.Matchmaking.Application.Abstractions;
using Kombats.Matchmaking.Domain;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Kombats.Matchmaking.Application.UseCases;

/// <summary>
/// Application service for matchmaking tick operations.
/// </summary>
public class MatchmakingService
{
    private readonly IMatchQueueStore _queueStore;
    private readonly IMatchRepository _matchRepository;
    private readonly IOutboxWriter _outboxWriter;
    private readonly ITransactionManager _transactionManager;
    private readonly ILogger<MatchmakingService> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public MatchmakingService(
        IMatchQueueStore queueStore,
        IMatchRepository matchRepository,
        IOutboxWriter outboxWriter,
        ITransactionManager transactionManager,
        ILogger<MatchmakingService> logger)
    {
        _queueStore = queueStore;
        _matchRepository = matchRepository;
        _outboxWriter = outboxWriter;
        _transactionManager = transactionManager;
        _logger = logger;
    }

    /// <summary>
    /// Performs a single matchmaking tick: tries to pop a pair and create a match.
    /// Uses transactional outbox pattern: all operations happen in one DB transaction.
    /// </summary>
    public async Task<MatchCreatedResult> MatchmakingTickAsync(string variant, CancellationToken cancellationToken = default)
    {
        // Try to pop a pair atomically
        var pair = await _queueStore.TryPopPairAsync(variant, cancellationToken);
        
        if (pair == null)
        {
            // No pair available
            return MatchCreatedResult.NoMatch;
        }

        var (playerAId, playerBId) = pair.Value;

        // Generate match and battle IDs
        var matchId = Guid.NewGuid();
        var battleId = Guid.NewGuid();

        var nowUtc = DateTime.UtcNow;

        // Use transactional outbox: in ONE DB transaction:
        // 1) Insert Match with state = BattleCreateRequested (or Created, then CAS update)
        // 2) Add outbox message for CreateBattle command
        // 3) Commit transaction
        // Publication will happen later by OutboxDispatcher worker
        await using var transaction = await _transactionManager.BeginTransactionAsync(cancellationToken);
        try
        {
            // 1) Insert Match with state = BattleCreateRequested (directly in target state to simplify)
            var match = new Match
            {
                MatchId = matchId,
                BattleId = battleId,
                PlayerAId = playerAId,
                PlayerBId = playerBId,
                Variant = variant,
                State = MatchState.BattleCreateRequested,
                CreatedAtUtc = new DateTimeOffset(nowUtc, TimeSpan.Zero),
                UpdatedAtUtc = new DateTimeOffset(nowUtc, TimeSpan.Zero)
            };

            await _matchRepository.InsertAsync(match, cancellationToken);

            // 2) Add outbox message for CreateBattle command
            var createBattleCommand = new
            {
                BattleId = battleId,
                MatchId = matchId,
                PlayerAId = playerAId,
                PlayerBId = playerBId,
                RequestedAt = new DateTimeOffset(nowUtc, TimeSpan.Zero)
            };

            var messagePayload = JsonSerializer.Serialize(createBattleCommand, JsonOptions);

            var outboxMessage = new OutboxMessage
            {
                Id = Guid.NewGuid(),
                OccurredAtUtc = nowUtc,
                Type = "Kombats.Contracts.Battle:CreateBattle",
                Payload = messagePayload,
                CorrelationId = matchId // Use matchId as correlation for tracking
            };

            await _outboxWriter.EnqueueAsync(outboxMessage, cancellationToken);

            // 3) Save all changes and commit transaction atomically
            // Note: SaveChangesAsync is called in InsertAsync and EnqueueAsync, but both use same DbContext
            // The transaction ensures atomicity
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Created match and enqueued CreateBattle command in outbox: MatchId={MatchId}, BattleId={BattleId}, PlayerA={PlayerAId}, PlayerB={PlayerBId}, Variant={Variant}",
                matchId, battleId, playerAId, playerBId, variant);

            return MatchCreatedResult.MatchCreated(new MatchCreatedInfo
            {
                MatchId = matchId,
                BattleId = battleId,
                PlayerAId = playerAId,
                PlayerBId = playerBId
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex,
                "Failed to create match and enqueue CreateBattle command: MatchId={MatchId}, BattleId={BattleId}, PlayerA={PlayerAId}, PlayerB={PlayerBId}",
                matchId, battleId, playerAId, playerBId);
            throw;
        }
    }
}

public enum MatchCreatedResultType
{
    NoMatch,
    MatchCreated
}