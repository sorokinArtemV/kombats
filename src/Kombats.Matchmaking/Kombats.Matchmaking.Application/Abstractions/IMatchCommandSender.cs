using Kombats.Contracts.Battle;

namespace Kombats.Matchmaking.Application.Abstractions;

/// <summary>
/// Port for sending CreateBattle commands.
/// Abstraction to avoid Application layer depending on MassTransit directly.
/// </summary>
public interface IMatchCommandSender
{
    /// <summary>
    /// Sends a CreateBattle command.
    /// </summary>
    Task SendCreateBattleAsync(CreateBattle command, CancellationToken cancellationToken = default);
}




