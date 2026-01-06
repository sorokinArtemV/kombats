namespace Combats.Battle.Domain;

/// <summary>
/// Battle engine interface - pure domain logic for resolving turns.
/// This is a pure function: given current state and actions, returns new state and events.
/// No infrastructure dependencies (Redis, MassTransit, SignalR, etc.).
/// </summary>
public interface IBattleEngine
{
    /// <summary>
    /// Resolves a turn given the current battle state and actions from both players.
    /// </summary>
    /// <param name="state">Current battle state</param>
    /// <param name="playerAAction">Action from player A</param>
    /// <param name="playerBAction">Action from player B</param>
    /// <returns>Resolution result with new state and domain events</returns>
    BattleResolutionResult ResolveTurn(
        BattleDomainState state,
        PlayerAction playerAAction,
        PlayerAction playerBAction);
}


