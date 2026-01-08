using Combats.Battle.Application.Abstractions;
using Combats.Battle.Application.ReadModels;
using Combats.Battle.Domain.Model;
using Microsoft.Extensions.Logging;

namespace Combats.Battle.Application.Protocol;

/// <summary>
/// Application-layer service for normalizing player action payloads.
/// Validates protocol (turn index, phase) and converts invalid payloads to NoAction.
/// </summary>
public class PlayerActionNormalizer
{
    private readonly IClock _clock;
    private readonly ILogger<PlayerActionNormalizer> _logger;

    public PlayerActionNormalizer(
        IClock clock,
        ILogger<PlayerActionNormalizer> logger)
    {
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Normalizes an action payload for a given battle state.
    /// Validates:
    /// - Battle is in TurnOpen phase
    /// - TurnIndex matches current server turn
    /// - Deadline hasn't passed
    /// - Payload is valid JSON (if not empty)
    /// 
    /// Returns normalized payload string (empty string = NoAction).
    /// </summary>
    public string NormalizeActionPayload(
        BattleSnapshot state,
        int clientTurnIndex,
        string? actionPayload,
        Guid playerId)
    {
        // Validate phase: must be TurnOpen
        if (state.Phase != BattlePhase.TurnOpen)
        {
            _logger.LogWarning(
                "Invalid phase for action submission: BattleId: {BattleId}, Phase: {Phase}, PlayerId: {PlayerId}, TurnIndex: {TurnIndex}",
                state.BattleId, state.Phase, playerId, clientTurnIndex);
            return string.Empty; // NoAction
        }

        // Validate turn index matches
        if (state.TurnIndex != clientTurnIndex)
        {
            _logger.LogWarning(
                "TurnIndex mismatch for action submission: BattleId: {BattleId}, Expected: {ExpectedTurnIndex}, Received: {ReceivedTurnIndex}, PlayerId: {PlayerId}",
                state.BattleId, state.TurnIndex, clientTurnIndex, playerId);
            return string.Empty; // NoAction
        }

        // Validate deadline hasn't passed (with small buffer for network latency)
        if (_clock.UtcNow > state.DeadlineUtc.AddSeconds(1))
        {
            _logger.LogWarning(
                "Deadline passed for action submission: BattleId: {BattleId}, TurnIndex: {TurnIndex}, DeadlineUtc: {DeadlineUtc}, PlayerId: {PlayerId}",
                state.BattleId, clientTurnIndex, state.DeadlineUtc, playerId);
            return string.Empty; // NoAction
        }

        // Validate payload: if empty/whitespace, treat as NoAction
        if (string.IsNullOrWhiteSpace(actionPayload))
        {
            _logger.LogInformation(
                "Empty action payload for BattleId: {BattleId}, TurnIndex: {TurnIndex}, PlayerId: {PlayerId}. Treating as NoAction.",
                state.BattleId, clientTurnIndex, playerId);
            return string.Empty; // NoAction
        }

        // Validate JSON format
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(actionPayload);
            // JSON is valid, return as-is (domain will validate zones)
            return actionPayload;
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "Invalid JSON in action payload for BattleId: {BattleId}, TurnIndex: {TurnIndex}, PlayerId: {PlayerId}. Treating as NoAction.",
                state.BattleId, clientTurnIndex, playerId);
            return string.Empty; // NoAction
        }
    }
}


