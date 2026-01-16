using Kombats.Battle.Domain.Rules;

namespace Kombats.Battle.Domain.Model;

/// <summary>
/// Domain representation of battle state.
/// This is a pure domain model, independent of infrastructure (Redis, JSON serialization, etc.).
/// </summary>
public sealed class BattleDomainState
{
    public Guid BattleId { get; init; }
    public Guid MatchId { get; init; }
    public Guid PlayerAId { get; init; }
    public Guid PlayerBId { get; init; }
    public Ruleset Ruleset { get; init; }
    public BattlePhase Phase { get; private set; }
    public int TurnIndex { get; private set; }
    public int NoActionStreakBoth { get; private set; }
    public int LastResolvedTurnIndex { get; private set; }
    public PlayerState PlayerA { get; private set; }
    public PlayerState PlayerB { get; private set; }

    public BattleDomainState(
        Guid battleId,
        Guid matchId,
        Guid playerAId,
        Guid playerBId,
        Ruleset ruleset,
        BattlePhase phase,
        int turnIndex,
        int noActionStreakBoth,
        int lastResolvedTurnIndex,
        PlayerState playerA,
        PlayerState playerB)
    {
        BattleId = battleId;
        MatchId = matchId;
        PlayerAId = playerAId;
        PlayerBId = playerBId;
        Ruleset = ruleset;
        Phase = phase;
        TurnIndex = turnIndex;
        NoActionStreakBoth = noActionStreakBoth;
        LastResolvedTurnIndex = lastResolvedTurnIndex;
        PlayerA = playerA;
        PlayerB = playerB;
    }
    
    public void EndBattle()
    {
        Phase = BattlePhase.Ended;
    }

    public void UpdateNoActionStreak(int streak)
    {
        NoActionStreakBoth = streak;
    }
}