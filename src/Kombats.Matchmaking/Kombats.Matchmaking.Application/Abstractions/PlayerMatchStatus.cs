using Kombats.Matchmaking.Domain;

namespace Kombats.Matchmaking.Application.Abstractions;

/// <summary>
/// Player match status enumeration.
/// </summary>
public enum PlayerMatchState
{
    Searching = 0,
    Matched = 1
}

/// <summary>
/// Player match status model.
/// </summary>
public class PlayerMatchStatus
{
    public required PlayerMatchState State { get; init; }
    public Guid? MatchId { get; init; }
    public Guid? BattleId { get; init; }
    public required string Variant { get; init; }
    public required DateTimeOffset UpdatedAtUtc { get; init; }
    public MatchState? MatchState { get; init; }
}

