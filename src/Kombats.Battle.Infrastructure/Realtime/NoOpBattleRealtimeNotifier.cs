using Kombats.Battle.Application.Abstractions;
using Kombats.Battle.Domain.Rules;

namespace Kombats.Battle.Infrastructure.Realtime;

public sealed class NoOpBattleRealtimeNotifier : IBattleRealtimeNotifier
{
    
    public async Task NotifyBattleReadyAsync(Guid battleId, Guid playerAId, Guid playerBId,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public async Task NotifyTurnOpenedAsync(Guid battleId, int turnIndex, DateTime deadlineUtc,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public async Task NotifyTurnResolvedAsync(Guid battleId, int turnIndex, string playerAAction, string playerBAction,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public async Task NotifyPlayerDamagedAsync(Guid battleId, Guid playerId, int damage, int remainingHp, int turnIndex,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public async Task NotifyBattleStateUpdatedAsync(Guid battleId, Guid playerAId, Guid playerBId, Ruleset ruleset, string phase,
        int turnIndex, DateTime deadlineUtc, int noActionStreakBoth, int lastResolvedTurnIndex, string? endedReason,
        int version, int? playerAHp, int? playerBHp, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public async Task NotifyBattleEndedAsync(Guid battleId, string reason, Guid? winnerPlayerId, DateTime endedAt,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}