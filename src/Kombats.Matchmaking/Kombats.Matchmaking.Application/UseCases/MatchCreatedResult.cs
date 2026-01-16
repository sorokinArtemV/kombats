namespace Kombats.Matchmaking.Application.UseCases;

/// <summary>
/// Result of matchmaking tick operation.
/// </summary>
public class MatchCreatedResult
{
    public required MatchCreatedResultType Type { get; init; }
    public MatchCreatedInfo? MatchInfo { get; init; }

    public static MatchCreatedResult NoMatch => new() { Type = MatchCreatedResultType.NoMatch };
    public static MatchCreatedResult MatchCreated(MatchCreatedInfo matchInfo) => new()
    {
        Type = MatchCreatedResultType.MatchCreated,
        MatchInfo = matchInfo
    };
}