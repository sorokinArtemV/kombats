namespace Combats.Services.Battle.State;

public interface IBattleStateStore
{
    public Task<bool> TryInitializeBattleAsync(Guid battleId, BattleState initialState, CancellationToken cancellationToken = default);
    public Task<BattleState?> GetStateAsync(Guid battleId, CancellationToken cancellationToken = default);
    public Task<bool> TryOpenTurnAsync(Guid battleId, int turnIndex, DateTime deadlineUtc, CancellationToken cancellationToken = default);
    public Task<bool> TryMarkTurnResolvingAsync(Guid battleId, int turnIndex, CancellationToken cancellationToken = default);
    public Task<bool> MarkTurnResolvedAndOpenNextAsync(Guid battleId, int currentTurnIndex, int nextTurnIndex, DateTime nextDeadlineUtc, int noActionStreak, CancellationToken cancellationToken = default);
    public Task<bool> EndBattleAndMarkResolvedAsync(Guid battleId, int turnIndex, int noActionStreak, CancellationToken cancellationToken = default);
    public Task<List<Guid>> GetActiveBattlesAsync(CancellationToken cancellationToken = default);
    
    // Deadline index methods (Redis ZSET)
    public Task AddBattleDeadlineAsync(Guid battleId, DateTime deadlineUtc, CancellationToken cancellationToken = default);
    public Task RemoveBattleDeadlineAsync(Guid battleId, CancellationToken cancellationToken = default);
    public Task<List<Guid>> GetDueBattlesAsync(DateTime nowUtc, int limit, CancellationToken cancellationToken = default);
    public Task StoreActionAsync(Guid battleId, int turnIndex, Guid playerId, string actionPayload, CancellationToken cancellationToken = default);
    public Task<(string? PlayerAAction, string? PlayerBAction)> GetActionsAsync(Guid battleId, int turnIndex, Guid playerAId, Guid playerBId, CancellationToken cancellationToken = default);
    public Task<bool> UpdatePlayerHpAsync(Guid battleId, int playerAHp, int playerBHp, CancellationToken cancellationToken = default);
}

