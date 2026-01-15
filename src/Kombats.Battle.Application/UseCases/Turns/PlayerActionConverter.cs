using Kombats.Battle.Domain.Model;

namespace Kombats.Battle.Application.UseCases.Turns;

/// <summary>
/// Converts canonical action commands to domain PlayerAction objects.
/// This is the boundary between Application and Domain layers.
/// </summary>
public static class PlayerActionConverter
{
    /// <summary>
    /// Converts a canonical action command to a domain PlayerAction.
    /// NoAction commands result in PlayerAction.NoAction.
    /// Valid commands are converted using PlayerAction.Create (which validates block patterns).
    /// </summary>
    public static PlayerAction ToDomainAction(PlayerActionCommand command)
    {
        // If it's a NoAction (by quality or missing attack zone), return NoAction
        if (command.IsNoAction)
        {
            return PlayerAction.NoAction(command.PlayerId, command.TurnIndex);
        }

        // Convert to domain action (PlayerAction.Create will validate block patterns)
        return PlayerAction.Create(
            command.PlayerId,
            command.TurnIndex,
            command.AttackZone,
            command.BlockZonePrimary,
            command.BlockZoneSecondary);
    }
}

