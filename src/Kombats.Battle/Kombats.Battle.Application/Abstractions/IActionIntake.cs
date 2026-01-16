using Kombats.Battle.Application.UseCases.Turns;

namespace Kombats.Battle.Application.Abstractions;

/// <summary>
/// Service for processing raw action payloads into canonical action representations.
/// Handles wire-level parsing (JSON), protocol validation, and semantic validation.
/// Invalid payloads are normalized to NoAction with appropriate quality/reason.
/// </summary>
public interface IActionIntake
{
    /// <summary>
    /// Processes a raw action payload into a canonical action command.
    /// 
    /// Validates:
    /// - JSON format (wire-level)
    /// - Protocol (phase, turn index, deadline)
    /// - Semantics (zones, block patterns)
    /// 
    /// Invalid payloads result in NoAction with appropriate quality/reason.
    /// </summary>
    /// <param name="battleId">Battle identifier</param>
    /// <param name="playerId">Player identifier</param>
    /// <param name="clientTurnIndex">Turn index from client</param>
    /// <param name="rawPayload">Raw JSON payload from client (may be null/empty)</param>
    /// <param name="battleState">Current battle state for protocol validation</param>
    /// <returns>Canonical action command (always valid, even if NoAction)</returns>
    PlayerActionCommand ProcessAction(
        Guid battleId,
        Guid playerId,
        int clientTurnIndex,
        string? rawPayload,
        Application.ReadModels.BattleSnapshot battleState);
}

