using Kombats.Matchmaking.Application.Abstractions;
using Kombats.Matchmaking.Domain;
using Microsoft.Extensions.Logging;

namespace Kombats.Matchmaking.Application.UseCases;

/// <summary>
/// Application service for matchmaking tick operations.
/// </summary>
public class MatchmakingService
{
    private readonly IMatchQueueStore _queueStore;
    private readonly IMatchRepository _matchRepository;
    private readonly ILogger<MatchmakingService> _logger;

    public MatchmakingService(
        IMatchQueueStore queueStore,
        IMatchRepository matchRepository,
        ILogger<MatchmakingService> logger)
    {
        _queueStore = queueStore;
        _matchRepository = matchRepository;
        _logger = logger;
    }

    /// <summary>
    /// Performs a single matchmaking tick: tries to pop a pair and create a match.
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

        var nowUtc = DateTimeOffset.UtcNow;

        // Create match entity
        var match = new Match
        {
            MatchId = matchId,
            BattleId = battleId,
            PlayerAId = playerAId,
            PlayerBId = playerBId,
            Variant = variant,
            State = MatchState.Created,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        // Create CreateBattle command
        var createBattleCommand = new Kombats.Contracts.Battle.CreateBattle
        {
            BattleId = battleId,
            MatchId = matchId,
            PlayerAId = playerAId,
            PlayerBId = playerBId,
            RequestedAt = nowUtc
        };

        // In ONE DB transaction: Insert Match, Send Command (outbox), Update State
        await _matchRepository.CreateMatchAndSendCommandAsync(
            match,
            createBattleCommand,
            MatchState.BattleCreateRequested,
            cancellationToken);

        _logger.LogInformation(
            "Created match and sent CreateBattle command: MatchId={MatchId}, BattleId={BattleId}, PlayerA={PlayerAId}, PlayerB={PlayerBId}, Variant={Variant}",
            matchId, battleId, playerAId, playerBId, variant);

        return MatchCreatedResult.MatchCreated(new MatchCreatedInfo
        {
            MatchId = matchId,
            BattleId = battleId,
            PlayerAId = playerAId,
            PlayerBId = playerBId
        });
    }
}

/// <summary>
/// Result of matchmaking tick operation.
/// </summary>
public class MatchCreatedResult
{
    public required MatchCreatedResultType Type { get; init; }
    public MatchCreatedInfo? MatchInfo { get; init; }

    public static MatchCreatedResult NoMatch => new() { Type = MatchCreatedResultType.NoMatch };
    public static MatchCreatedResult MatchCreated(MatchCreatedInfo matchInfo) => new()
    {
        Type = MatchCreatedResultType.MatchCreated,
        MatchInfo = matchInfo
    };
}

public enum MatchCreatedResultType
{
    NoMatch,
    MatchCreated
}

public class MatchCreatedInfo
{
    public required Guid MatchId { get; init; }
    public required Guid BattleId { get; init; }
    public required Guid PlayerAId { get; init; }
    public required Guid PlayerBId { get; init; }
}

