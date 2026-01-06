using Combats.Contracts.Battle;

namespace Combats.Battle.Domain.Model;

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
    public Ruleset Ruleset { get; init; } = null!;
    public BattlePhase Phase { get; private set; }
    public int TurnIndex { get; private set; }
    public int NoActionStreakBoth { get; private set; }
    public int LastResolvedTurnIndex { get; private set; }
    public PlayerState PlayerA { get; private set; } = null!;
    public PlayerState PlayerB { get; private set; } = null!;

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

    public void AdvanceToNextTurn(int nextTurnIndex)
    {
        if (Phase != BattlePhase.Resolving)
            throw new InvalidOperationException($"Cannot advance turn from phase {Phase}");

        Phase = BattlePhase.TurnOpen;
        TurnIndex = nextTurnIndex;
        LastResolvedTurnIndex = TurnIndex - 1;
    }

    public void EndBattle()
    {
        Phase = BattlePhase.Ended;
    }

    public void UpdateNoActionStreak(int streak)
    {
        NoActionStreakBoth = streak;
    }

    public PlayerState GetPlayerState(Guid playerId)
    {
        if (playerId == PlayerAId)
            return PlayerA;
        if (playerId == PlayerBId)
            return PlayerB;
        throw new ArgumentException($"Player {playerId} is not a participant in battle {BattleId}", nameof(playerId));
    }

    public PlayerState GetOpponentState(Guid playerId)
    {
        if (playerId == PlayerAId)
            return PlayerB;
        if (playerId == PlayerBId)
            return PlayerA;
        throw new ArgumentException($"Player {playerId} is not a participant in battle {BattleId}", nameof(playerId));
    }
}

/// <summary>
/// Battle phase enum (domain-level, matches infrastructure enum).
/// </summary>
public enum BattlePhase
{
    ArenaOpen = 0,
    TurnOpen = 1,
    Resolving = 2,
    Ended = 3
}


