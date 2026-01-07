namespace Combats.Battle.Application.Rules;

/// <summary>
/// Default values and bounds for battle rules configuration.
/// Single source of truth for battle rules defaults and validation bounds.
/// </summary>
public class BattleRulesDefaults
{
    // Default values
    public int DefaultTurnSeconds { get; init; } = 10;
    public int DefaultNoActionLimit { get; init; } = 3;
    public int DefaultHpPerStamina { get; init; } = 10;
    public int DefaultDamagePerStrength { get; init; } = 2;

    // Validation bounds
    public int MinTurnSeconds { get; init; } = 1;
    public int MaxTurnSeconds { get; init; } = 60;
    public int MinNoActionLimit { get; init; } = 1;
    public int MaxNoActionLimit { get; init; } = 10;
    public int MinHpPerStamina { get; init; } = 1;
    public int MinDamagePerStrength { get; init; } = 1;
}

