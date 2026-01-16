using Kombats.Battle.Domain.Rules;

namespace Kombats.Battle.Application.UseCases.Turns;

/// <summary>
/// Canonical representation of a player action after intake processing.
/// This is the normalized, validated action that gets stored in Redis.
/// Invalid/malformed actions are represented as NoAction with appropriate quality/reason.
/// </summary>
public sealed record PlayerActionCommand
{
    public Guid BattleId { get; init; }
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
    /// Quality of the action (Valid, NoAction, Invalid, Late, ProtocolViolation).
    /// </summary>
    public ActionQuality Quality { get; init; }
    
    /// <summary>
    /// Optional reason for rejection (for logging/telemetry only).
    /// </summary>
    public ActionRejectReason? RejectReason { get; init; }
    
    /// <summary>
    /// True if this is a NoAction (no valid action submitted).
    /// </summary>
    public bool IsNoAction => Quality != ActionQuality.Valid || AttackZone == null;

    /// <summary>
    /// Validates the invariant: if Quality == Valid, then AttackZone must be non-null.
    /// Throws InvalidOperationException if invariant is violated.
    /// </summary>
    public void ValidateInvariant()
    {
        if (Quality == ActionQuality.Valid && AttackZone == null)
        {
            throw new InvalidOperationException(
                $"Invariant violation: Quality is {Quality} but AttackZone is null. " +
                $"BattleId: {BattleId}, PlayerId: {PlayerId}, TurnIndex: {TurnIndex}");
        }
    }
}

/// <summary>
/// Quality classification of an action after intake processing.
/// </summary>
public enum ActionQuality
{
    /// <summary>
    /// Valid action with proper zones.
    /// </summary>
    Valid,
    
    /// <summary>
    /// No action submitted (empty payload or explicit NoAction).
    /// </summary>
    NoAction,
    
    /// <summary>
    /// Invalid payload (malformed JSON, invalid zones, invalid block pattern).
    /// </summary>
    Invalid,
    
    /// <summary>
    /// Action submitted after deadline.
    /// </summary>
    Late,
    
    /// <summary>
    /// Protocol violation (wrong phase, wrong turn index, etc.).
    /// </summary>
    ProtocolViolation
}

/// <summary>
/// Detailed reason for action rejection (for observability).
/// </summary>
public enum ActionRejectReason
{
    EmptyPayload,
    InvalidJson,
    InvalidAttackZone,
    InvalidBlockZonePrimary,
    InvalidBlockZoneSecondary,
    InvalidBlockPattern,
    WrongPhase,
    WrongTurnIndex,
    DeadlinePassed,
    MissingAttackZone,
    InvariantViolation
}

