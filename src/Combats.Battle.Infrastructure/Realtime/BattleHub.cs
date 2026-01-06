using Microsoft.AspNetCore.SignalR;

namespace Combats.Battle.Infrastructure.Realtime;

/// <summary>
/// Base SignalR hub for battle realtime communication.
/// This is an Infrastructure component (depends on SignalR).
/// Api layer extends this hub with specific methods.
/// </summary>
public class BattleHub : Hub
{
    // Base hub - Api layer extends this with JoinBattle, SubmitTurnAction, etc.
}


