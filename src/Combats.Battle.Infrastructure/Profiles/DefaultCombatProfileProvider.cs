using Combats.Battle.Application.Ports;
using Microsoft.Extensions.Logging;

namespace Combats.Battle.Infrastructure.Profiles;

/// <summary>
/// Default implementation of ICombatProfileProvider.
/// Returns default stats until real DB/projection exists.
/// </summary>
public class DefaultCombatProfileProvider : ICombatProfileProvider
{
    private readonly ILogger<DefaultCombatProfileProvider> _logger;
    private const int DefaultStrength = 10;
    private const int DefaultStamina = 10;

    public DefaultCombatProfileProvider(ILogger<DefaultCombatProfileProvider> logger)
    {
        _logger = logger;
    }

    public Task<CombatProfile?> GetProfileAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Returning default combat profile for PlayerId: {PlayerId} (Strength: {Strength}, Stamina: {Stamina})",
            playerId, DefaultStrength, DefaultStamina);

        return Task.FromResult<CombatProfile?>(
            new CombatProfile(playerId, DefaultStrength, DefaultStamina));
    }
}


