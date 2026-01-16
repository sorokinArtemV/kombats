using System.Text.Json;
using Kombats.Battle.Application.Abstractions;
using Kombats.Battle.Application.ReadModels;
using Kombats.Battle.Domain.Model;
using Kombats.Battle.Domain.Rules;
using Microsoft.Extensions.Logging;

namespace Kombats.Battle.Application.UseCases.Turns;

/// <summary>
/// Implementation of action intake pipeline.
/// Separates wire-level parsing (JSON) from domain logic.
/// All invalid payloads are normalized to NoAction with appropriate quality/reason.
/// </summary>
public class ActionIntakeService : IActionIntake
{
    private readonly IClock _clock;
    private readonly ILogger<ActionIntakeService> _logger;

    public ActionIntakeService(
        IClock clock,
        ILogger<ActionIntakeService> logger)
    {
        _clock = clock;
        _logger = logger;
    }

    public PlayerActionCommand ProcessAction(
        Guid battleId,
        Guid playerId,
        int clientTurnIndex,
        string? rawPayload,
        BattleSnapshot battleState)
    {
        // Step 1: Protocol validation (phase, turn index, deadline)
        var protocolResult = ValidateProtocol(battleState, clientTurnIndex, playerId);
        if (protocolResult != null)
        {
            return protocolResult;
        }

        // Step 2: Payload validation (empty/whitespace)
        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            _logger.LogDebug(
                "Empty action payload for BattleId: {BattleId}, TurnIndex: {TurnIndex}, PlayerId: {PlayerId}. Treating as NoAction.",
                battleId, clientTurnIndex, playerId);
            return CreateNoAction(battleId, playerId, battleState.TurnIndex, ActionRejectReason.EmptyPayload);
        }

        // Step 3: JSON parsing (wire-level)
        JsonDocument? jsonDoc = null;
        try
        {
            jsonDoc = JsonDocument.Parse(rawPayload);
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(
                ex,
                "Invalid JSON in action payload for BattleId: {BattleId}, TurnIndex: {TurnIndex}, PlayerId: {PlayerId}. Treating as NoAction.",
                battleId, clientTurnIndex, playerId);
            return CreateNoAction(battleId, playerId, battleState.TurnIndex, ActionRejectReason.InvalidJson);
        }

        // Step 4: Parse zones from JSON (semantic validation)
        using (jsonDoc)
        {
            var root = jsonDoc.RootElement;
            BattleZone? attackZone = null;
            BattleZone? blockZonePrimary = null;
            BattleZone? blockZoneSecondary = null;
            ActionRejectReason? rejectReason = null;

            // Parse attack zone
            if (root.TryGetProperty("attackZone", out var attackZoneElement))
            {
                var attackZoneStr = attackZoneElement.GetString();
                if (!string.IsNullOrWhiteSpace(attackZoneStr) &&
                    Enum.TryParse<BattleZone>(attackZoneStr, ignoreCase: true, out var parsedAttackZone))
                {
                    attackZone = parsedAttackZone;
                }
                else if (!string.IsNullOrWhiteSpace(attackZoneStr))
                {
                    rejectReason = ActionRejectReason.InvalidAttackZone;
                }
            }

            // Parse block zones
            if (root.TryGetProperty("blockZonePrimary", out var blockPrimaryElement))
            {
                var blockPrimaryStr = blockPrimaryElement.GetString();
                if (!string.IsNullOrWhiteSpace(blockPrimaryStr) &&
                    Enum.TryParse<BattleZone>(blockPrimaryStr, ignoreCase: true, out var parsedBlockPrimary))
                {
                    blockZonePrimary = parsedBlockPrimary;
                }
                else if (!string.IsNullOrWhiteSpace(blockPrimaryStr))
                {
                    rejectReason = rejectReason ?? ActionRejectReason.InvalidBlockZonePrimary;
                }
            }

            if (root.TryGetProperty("blockZoneSecondary", out var blockSecondaryElement))
            {
                var blockSecondaryStr = blockSecondaryElement.GetString();
                if (!string.IsNullOrWhiteSpace(blockSecondaryStr) &&
                    Enum.TryParse<BattleZone>(blockSecondaryStr, ignoreCase: true, out var parsedBlockSecondary))
                {
                    blockZoneSecondary = parsedBlockSecondary;
                }
                else if (!string.IsNullOrWhiteSpace(blockSecondaryStr))
                {
                    rejectReason = rejectReason ?? ActionRejectReason.InvalidBlockZoneSecondary;
                }
            }

            // If no attack zone, it's NoAction
            if (attackZone == null)
            {
                return CreateNoAction(
                    battleId,
                    playerId,
                    battleState.TurnIndex,
                    rejectReason ?? ActionRejectReason.MissingAttackZone);
            }

            // Validate block pattern if both block zones are provided
            if (blockZonePrimary != null && blockZoneSecondary != null)
            {
                if (!BattleZoneHelper.IsValidBlockPattern(blockZonePrimary.Value, blockZoneSecondary.Value))
                {
                    return CreateNoAction(battleId, playerId, battleState.TurnIndex, ActionRejectReason.InvalidBlockPattern);
                }
            }

            // Valid action - enforce invariant: Quality == Valid => AttackZone != null
            if (attackZone == null)
            {
                _logger.LogError(
                    "Invariant violation: Attempted to create Valid action with null AttackZone for BattleId: {BattleId}, PlayerId: {PlayerId}, TurnIndex: {TurnIndex}. Converting to Invalid.",
                    battleId, playerId, battleState.TurnIndex);
                return CreateNoAction(battleId, playerId, battleState.TurnIndex, ActionRejectReason.InvariantViolation);
            }

            var command = new PlayerActionCommand
            {
                BattleId = battleId,
                PlayerId = playerId,
                TurnIndex = battleState.TurnIndex, // Use server turn index, not client
                AttackZone = attackZone,
                BlockZonePrimary = blockZonePrimary,
                BlockZoneSecondary = blockZoneSecondary,
                Quality = ActionQuality.Valid,
                RejectReason = null
            };
            
            // Validate invariant before returning
            command.ValidateInvariant();
            return command;
        }
    }

    /// <summary>
    /// Validates protocol-level constraints (phase, turn index, deadline).
    /// Returns NoAction command if validation fails, null if validation passes.
    /// </summary>
    private PlayerActionCommand? ValidateProtocol(BattleSnapshot state, int clientTurnIndex, Guid playerId)
    {
        // Validate phase: must be TurnOpen
        if (state.Phase != BattlePhase.TurnOpen)
        {
            _logger.LogDebug(
                "Invalid phase for action submission: BattleId: {BattleId}, Phase: {Phase}, PlayerId: {PlayerId}, TurnIndex: {TurnIndex}",
                state.BattleId, state.Phase, playerId, clientTurnIndex);
            return CreateNoAction(state.BattleId, playerId, state.TurnIndex, ActionRejectReason.WrongPhase);
        }

        // Validate turn index matches
        if (state.TurnIndex != clientTurnIndex)
        {
            _logger.LogDebug(
                "TurnIndex mismatch for action submission: BattleId: {BattleId}, Expected: {ExpectedTurnIndex}, Received: {ReceivedTurnIndex}, PlayerId: {PlayerId}",
                state.BattleId, state.TurnIndex, clientTurnIndex, playerId);
            return CreateNoAction(state.BattleId, playerId, state.TurnIndex, ActionRejectReason.WrongTurnIndex);
        }

        // Validate deadline hasn't passed (with small buffer for network latency)
        if (_clock.UtcNow > state.DeadlineUtc.AddSeconds(1))
        {
            _logger.LogDebug(
                "Deadline passed for action submission: BattleId: {BattleId}, TurnIndex: {TurnIndex}, DeadlineUtc: {DeadlineUtc}, PlayerId: {PlayerId}",
                state.BattleId, clientTurnIndex, state.DeadlineUtc, playerId);
            return CreateNoAction(state.BattleId, playerId, state.TurnIndex, ActionRejectReason.DeadlinePassed);
        }

        return null; // Protocol validation passed
    }

    /// <summary>
    /// Creates a NoAction command with appropriate quality based on reject reason.
    /// </summary>
    private static PlayerActionCommand CreateNoAction(
        Guid battleId,
        Guid playerId,
        int turnIndex,
        ActionRejectReason reason)
    {
        var quality = reason switch
        {
            ActionRejectReason.EmptyPayload => ActionQuality.NoAction,
            ActionRejectReason.DeadlinePassed => ActionQuality.Late,
            ActionRejectReason.WrongPhase or ActionRejectReason.WrongTurnIndex => ActionQuality.ProtocolViolation,
            ActionRejectReason.InvariantViolation => ActionQuality.Invalid,
            _ => ActionQuality.Invalid
        };

        return new PlayerActionCommand
        {
            BattleId = battleId,
            PlayerId = playerId,
            TurnIndex = turnIndex,
            AttackZone = null,
            BlockZonePrimary = null,
            BlockZoneSecondary = null,
            Quality = quality,
            RejectReason = reason
        };
    }
}

