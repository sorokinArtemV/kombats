namespace Kombats.Battle.Domain.Rules;

/// <summary>
/// Battle ruleset - a domain value object that defines battle parameters.
/// This is the canonical source of truth for battle rules in the domain.
/// 
/// Fully immutable: constructed only via Ruleset.Create() with strict validation.
/// No init setters, no default values, no silent corrections.
/// </summary>
public sealed record Ruleset
{
    public int Version { get; }
    public int TurnSeconds { get; }
    public int NoActionLimit { get; }
    public int Seed { get; }

    // New combat balance system
    public CombatBalance Balance { get; }

    // Constructor - prefer Create() factory method for explicit validation
    // Made public to support JSON deserialization (System.Text.Json requires accessible constructor for records)
    // Properties are get-only, so the record remains immutable
    public Ruleset(
        int version,
        int turnSeconds,
        int noActionLimit,
        int seed,
        CombatBalance balance)
    {
        // Same validation as Create() - fail fast on invalid data
        if (version <= 0)
            throw new ArgumentException("Version must be greater than 0", nameof(version));
        if (turnSeconds <= 0)
            throw new ArgumentException("TurnSeconds must be greater than 0", nameof(turnSeconds));
        if (noActionLimit <= 0)
            throw new ArgumentException("NoActionLimit must be greater than 0", nameof(noActionLimit));
        if (balance == null)
            throw new ArgumentNullException(nameof(balance), "CombatBalance is required");

        Version = version;
        TurnSeconds = turnSeconds;
        NoActionLimit = noActionLimit;
        Seed = seed;
        Balance = balance;
    }

    /// <summary>
    /// Creates a Ruleset with strict validation.
    /// All parameters must be valid; no defaults, no clamping, no silent corrections.
    /// 
    /// Validation rules:
    /// - Version > 0
    /// - TurnSeconds > 0
    /// - NoActionLimit > 0
    /// - Seed must be explicitly provided (no implicit defaults)
    /// - HpPerStamina > 0
    /// - DamagePerStrength > 0
    /// - CombatBalance is required (not null)
    /// 
    /// Throws ArgumentException or ArgumentNullException on validation failure.
    /// 
    /// Note: The constructor also performs the same validation. This factory method
    /// is provided for clarity and explicit validation intent.
    /// </summary>
    public static Ruleset Create(
        int version,
        int turnSeconds,
        int noActionLimit,
        int seed,
        CombatBalance balance)
    {
        return new Ruleset(
            version,
            turnSeconds,
            noActionLimit,
            seed,
            balance);
    }
}





