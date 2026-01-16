using Kombats.Contracts.Battle;
using Kombats.Matchmaking.Application.Abstractions;
using Kombats.Matchmaking.Domain;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Kombats.Matchmaking.Infrastructure.Messaging.Consumers;

/// <summary>
/// Consumer for BattleCreated event.
/// Updates match state from BattleCreateRequested to BattleCreated (or Completed).
/// Uses inbox for idempotency (deduplication by MessageId).
/// </summary>
public class BattleCreatedConsumer : IConsumer<BattleCreated>
{
    private readonly IMatchRepository _matchRepository;
    private readonly ILogger<BattleCreatedConsumer> _logger;

    public BattleCreatedConsumer(
        IMatchRepository matchRepository,
        ILogger<BattleCreatedConsumer> logger)
    {
        _matchRepository = matchRepository;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<BattleCreated> context)
    {
        var @event = context.Message;
        var messageId = context.MessageId?.ToString() ?? "unknown";
        
        _logger.LogInformation(
            "Processing BattleCreated event for BattleId: {BattleId}, MatchId: {MatchId}, MessageId: {MessageId}",
            @event.BattleId, @event.MatchId, messageId);

        // Load match by MatchId
        var match = await _matchRepository.GetByMatchIdAsync(@event.MatchId, context.CancellationToken);
        
        if (match == null)
        {
            _logger.LogWarning(
                "Match {MatchId} not found for BattleCreated event. MessageId: {MessageId}",
                @event.MatchId, messageId);
            // ACK the message - match may have been deleted or doesn't exist
            return;
        }

        // If match already in BattleCreated/Completed, no-op (idempotent)
        if (match.State == MatchState.BattleCreated || match.State == MatchState.Completed)
        {
            _logger.LogInformation(
                "Match {MatchId} already in state {State}. BattleCreated event is duplicate (idempotent). MessageId: {MessageId}",
                @event.MatchId, match.State, messageId);
            return;
        }

        // Only update if match is in BattleCreateRequested state
        if (match.State != MatchState.BattleCreateRequested)
        {
            _logger.LogWarning(
                "Match {MatchId} is in unexpected state {State} for BattleCreated event. Expected BattleCreateRequested. MessageId: {MessageId}",
                @event.MatchId, match.State, messageId);
            // Still update to BattleCreated to converge state
        }

        // Update match state to BattleCreated
        var updatedAt = DateTimeOffset.UtcNow;
        await _matchRepository.UpdateStateAsync(
            @event.MatchId,
            MatchState.BattleCreated,
            updatedAt,
            context.CancellationToken);

        _logger.LogInformation(
            "Updated match state to BattleCreated: MatchId={MatchId}, BattleId={BattleId}, PlayerA={PlayerAId}, PlayerB={PlayerBId}, MessageId={MessageId}",
            @event.MatchId, @event.BattleId, @event.PlayerAId, @event.PlayerBId, messageId);
    }
}

