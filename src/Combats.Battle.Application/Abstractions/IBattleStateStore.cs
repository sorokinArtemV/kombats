using Combats.Battle.Application.ReadModels;
using Combats.Battle.Domain.Model;

namespace Combats.Battle.Application.Abstractions;

/// <summary>
/// Port interface for battle state persistence.
/// Application defines what it needs; Infrastructure provides implementation.
/// Works with domain models for writes and read models for queries.
/// </summary>
public interface IBattleStateStore
{
    Task<bool> TryInitializeBattleAsync(Guid battleId, BattleDomainState initialState, CancellationToken cancellationToken = default);
    Task<BattleSnapshot?> GetStateAsync(Guid battleId, CancellationToken cancellationToken = default);
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
    Task<EndBattleCommitResult> EndBattleAndMarkResolvedAsync(
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
    Task<ActionStoreResult> StoreActionAsync(Guid battleId, int turnIndex, Guid playerId, string actionPayload, CancellationToken cancellationToken = default);
    Task<(string? PlayerAAction, string? PlayerBAction)> GetActionsAsync(Guid battleId, int turnIndex, Guid playerAId, Guid playerBId, CancellationToken cancellationToken = default);
}




