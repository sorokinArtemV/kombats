using System.Text.Json;

namespace Combats.Services.Battle.Domain;

/// <summary>
/// Parser for converting action payloads (JSON strings) to domain PlayerAction.
/// This isolates domain from infrastructure concerns (JSON parsing).
/// </summary>
public static class ActionParser
{
    /// <summary>
    /// Parses an action payload string into a PlayerAction with zones.
    /// Expected JSON format:
    /// {
    ///   "attackZone": "Head" | "Chest" | "Belly" | "Waist" | "Legs",
    ///   "blockZonePrimary": "Head" | ... (optional),
    ///   "blockZoneSecondary": "Chest" | ... (optional)
    /// }
    /// If payload is empty/invalid, returns NoAction.
    /// </summary>
    public static PlayerAction ParseAction(string? payload, Guid playerId, int turnIndex)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return PlayerAction.NoAction(playerId, turnIndex);
        }

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            BattleZone? attackZone = null;
            BattleZone? blockZonePrimary = null;
            BattleZone? blockZoneSecondary = null;

            // Parse attack zone
            if (root.TryGetProperty("attackZone", out var attackZoneElement))
            {
                var attackZoneStr = attackZoneElement.GetString();
                if (Enum.TryParse<BattleZone>(attackZoneStr, ignoreCase: true, out var parsedAttackZone))
                {
                    attackZone = parsedAttackZone;
                }
            }

            // Parse block zones
            if (root.TryGetProperty("blockZonePrimary", out var blockPrimaryElement))
            {
                var blockPrimaryStr = blockPrimaryElement.GetString();
                if (Enum.TryParse<BattleZone>(blockPrimaryStr, ignoreCase: true, out var parsedBlockPrimary))
                {
                    blockZonePrimary = parsedBlockPrimary;
                }
            }

            if (root.TryGetProperty("blockZoneSecondary", out var blockSecondaryElement))
            {
                var blockSecondaryStr = blockSecondaryElement.GetString();
                if (Enum.TryParse<BattleZone>(blockSecondaryStr, ignoreCase: true, out var parsedBlockSecondary))
                {
                    blockZoneSecondary = parsedBlockSecondary;
                }
            }

            // Create action (validation happens in PlayerAction.Create)
            return PlayerAction.Create(playerId, turnIndex, attackZone, blockZonePrimary, blockZoneSecondary);
        }
        catch (JsonException)
        {
            // Invalid JSON - treat as NoAction
            return PlayerAction.NoAction(playerId, turnIndex);
        }
    }
}

