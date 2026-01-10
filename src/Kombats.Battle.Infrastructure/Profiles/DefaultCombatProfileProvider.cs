using Kombats.Battle.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Kombats.Battle.Infrastructure.Profiles;

/// <summary>
/// Default implementation of ICombatProfileProvider.
/// Returns default stats until real DB/projection exists.
/// </summary>
public class DefaultCombatProfileProvider : ICombatProfileProvider
{
    private readonly ILogger<DefaultCombatProfileProvider> _logger;
    private const int DefaultStrength = 10;
    private const int DefaultStamina = 10;
    private const int DefaultAgility = 0;
    private const int DefaultIntuition = 0;

    public DefaultCombatProfileProvider(ILogger<DefaultCombatProfileProvider> logger)
    {
        _logger = logger;
    }

    public Task<CombatProfile?> GetProfileAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Returning default combat profile for PlayerId: {PlayerId} (Strength: {Strength}, Stamina: {Stamina}, Agility: {Agility}, Intuition: {Intuition})",
            playerId, DefaultStrength, DefaultStamina, DefaultAgility, DefaultIntuition);

        return Task.FromResult<CombatProfile?>(
            new CombatProfile(playerId, DefaultStrength, DefaultStamina, DefaultAgility, DefaultIntuition));
    }
}









