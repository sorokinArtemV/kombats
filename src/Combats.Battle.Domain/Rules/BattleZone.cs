namespace Combats.Battle.Domain.Rules;

/// <summary>
/// Represents a body zone in fistfight combat.
/// Zones form a ring topology for adjacency validation.
/// </summary>
public enum BattleZone
{
    Head = 0,
    Chest = 1,
    Belly = 2,
    Waist = 3,
    Legs = 4
}

/// <summary>
/// Helper for zone adjacency validation.
/// </summary>
public static class BattleZoneHelper
{
    /// <summary>
    /// Valid block patterns (adjacent pairs in ring topology).
    /// </summary>
    private static readonly HashSet<(BattleZone, BattleZone)> ValidBlockPatterns = new()
    {
        (BattleZone.Head, BattleZone.Chest),
        (BattleZone.Chest, BattleZone.Belly),
        (BattleZone.Belly, BattleZone.Waist),
        (BattleZone.Waist, BattleZone.Legs),
        (BattleZone.Legs, BattleZone.Head)
    };

    /// <summary>
    /// Checks if two zones form a valid block pattern (adjacent in ring).
    /// </summary>
    public static bool IsValidBlockPattern(BattleZone zone1, BattleZone zone2)
    {
        // Check both orders (zone1-zone2 and zone2-zone1)
        return ValidBlockPatterns.Contains((zone1, zone2)) || 
               ValidBlockPatterns.Contains((zone2, zone1));
    }

    /// <summary>
    /// Checks if an attack zone is blocked by a block pattern.
    /// </summary>
    public static bool IsZoneBlocked(BattleZone attackZone, BattleZone? blockZone1, BattleZone? blockZone2)
    {
        if (blockZone1 == null || blockZone2 == null)
            return false;

        return attackZone == blockZone1 || attackZone == blockZone2;
    }

    /// <summary>
    /// Gets all valid block patterns as tuples.
    /// </summary>
    public static IReadOnlyList<(BattleZone, BattleZone)> GetValidBlockPatterns()
    {
        return ValidBlockPatterns.ToList();
    }
}


