namespace Kombats.Contracts.Battle;

public sealed record ResolveTurn(
    Guid BattleId,
    int TurnIndex
);

