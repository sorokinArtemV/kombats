namespace Kombats.Matchmaking.Application.UseCases;

public class MatchCreatedInfo
{
    public required Guid MatchId { get; init; }
    public required Guid BattleId { get; init; }
    public required Guid PlayerAId { get; init; }
    public required Guid PlayerBId { get; init; }
}