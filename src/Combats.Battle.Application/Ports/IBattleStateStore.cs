namespace Combats.Battle.Application.Ports;

/// <summary>
/// Port interface for battle state persistence.
/// Application defines what it needs; Infrastructure provides implementation.
/// </summary>
public interface IBattleStateStore
{
    Task<bool> TryInitializeBattleAsync(Guid battleId, BattleStateView initialState, CancellationToken cancellationToken = default);
    Task<BattleStateView?> GetStateAsync(Guid battleId, CancellationToken cancellationToken = default);
    Task<bool> TryOpenTurnAsync(Guid battleId, int turnIndex, DateTime deadlineUtc, CancellationToken cancellationToken = default);
    Task<bool> TryMarkTurnResolvingAsync(Guid battleId, int turnIndex, CancellationToken cancellationToken = default);
    Task<bool> MarkTurnResolvedAndOpenNextAsync(
        Guid battleId, 
        int currentTurnIndex, 
        int nextTurnIndex, 
        DateTime nextDeadlineUtc, 
        int noActionStreak,
        int playerAHp,
        int playerBHp,
        CancellationToken cancellationToken = default);
    Task<bool> EndBattleAndMarkResolvedAsync(
        Guid battleId, 
        int turnIndex, 
        int noActionStreak,
        int playerAHp,
        int playerBHp,
        CancellationToken cancellationToken = default);
    Task<List<Guid>> GetActiveBattlesAsync(CancellationToken cancellationToken = default);
    
    // Deadline index methods (Redis ZSET)
    Task AddBattleDeadlineAsync(Guid battleId, DateTime deadlineUtc, CancellationToken cancellationToken = default);
    Task RemoveBattleDeadlineAsync(Guid battleId, CancellationToken cancellationToken = default);
    Task<List<Guid>> GetDueBattlesAsync(DateTime nowUtc, int limit, CancellationToken cancellationToken = default);
    Task StoreActionAsync(Guid battleId, int turnIndex, Guid playerId, string actionPayload, CancellationToken cancellationToken = default);
    Task<(string? PlayerAAction, string? PlayerBAction)> GetActionsAsync(Guid battleId, int turnIndex, Guid playerAId, Guid playerBId, CancellationToken cancellationToken = default);
}


