namespace Combats.Services.Battle.Domain;

/// <summary>
/// Represents a player's action for a specific turn in fistfight combat.
/// Each turn: 1 attack zone + 2 adjacent zones for block.
/// </summary>
public sealed record PlayerAction
{
    public Guid PlayerId { get; init; }
    public int TurnIndex { get; init; }
    
    /// <summary>
    /// Attack zone (nullable if NoAction).
    /// </summary>
    public BattleZone? AttackZone { get; init; }
    
    /// <summary>
    /// Primary block zone (first of two adjacent zones).
    /// </summary>
    public BattleZone? BlockZonePrimary { get; init; }
    
    /// <summary>
    /// Secondary block zone (second of two adjacent zones).
    /// </summary>
    public BattleZone? BlockZoneSecondary { get; init; }
    
    /// <summary>
    /// True if this is a NoAction (no valid action submitted).
    /// </summary>
    public bool IsNoAction { get; init; }

    /// <summary>
    /// Creates a NoAction (invalid/missing action).
    /// </summary>
    public static PlayerAction NoAction(Guid playerId, int turnIndex)
    {
        return new PlayerAction
        {
            PlayerId = playerId,
            TurnIndex = turnIndex,
            IsNoAction = true,
            AttackZone = null,
            BlockZonePrimary = null,
            BlockZoneSecondary = null
        };
    }

    /// <summary>
    /// Creates an action with attack and block zones.
    /// Validates block pattern adjacency.
    /// </summary>
    public static PlayerAction Create(
        Guid playerId,
        int turnIndex,
        BattleZone? attackZone,
        BattleZone? blockZonePrimary,
        BattleZone? blockZoneSecondary)
    {
        // If no attack zone, it's NoAction
        if (attackZone == null)
        {
            return NoAction(playerId, turnIndex);
        }

        // If block zones are provided, validate adjacency
        if (blockZonePrimary != null && blockZoneSecondary != null)
        {
            if (!BattleZoneHelper.IsValidBlockPattern(blockZonePrimary.Value, blockZoneSecondary.Value))
            {
                // Invalid block pattern -> NoAction
                return NoAction(playerId, turnIndex);
            }
        }

        return new PlayerAction
        {
            PlayerId = playerId,
            TurnIndex = turnIndex,
            AttackZone = attackZone,
            BlockZonePrimary = blockZonePrimary,
            BlockZoneSecondary = blockZoneSecondary,
            IsNoAction = false
        };
    }
}

