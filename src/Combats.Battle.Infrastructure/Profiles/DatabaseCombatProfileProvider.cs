using Combats.Battle.Application.Ports;
using Combats.Battle.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Combats.Battle.Infrastructure.Profiles;

/// <summary>
/// Database implementation of ICombatProfileProvider.
/// Reads player combat profiles from PostgreSQL read model.
/// Falls back to defaults if profile not found (defensive).
/// </summary>
public class DatabaseCombatProfileProvider : ICombatProfileProvider
{
    private readonly BattleDbContext _dbContext;
    private readonly ILogger<DatabaseCombatProfileProvider> _logger;
    private const int DefaultStrength = 10;
    private const int DefaultStamina = 10;

    public DatabaseCombatProfileProvider(
        BattleDbContext dbContext,
        ILogger<DatabaseCombatProfileProvider> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<CombatProfile?> GetProfileAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        var profile = await _dbContext.PlayerProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PlayerId == playerId, cancellationToken);

        if (profile == null)
        {
            _logger.LogWarning(
                "Player profile not found for PlayerId: {PlayerId}. Using defaults (Strength: {Strength}, Stamina: {Stamina}). " +
                "Consider creating a projection consumer to populate player_profiles table from character service events.",
                playerId, DefaultStrength, DefaultStamina);

            // Fallback to defaults (defensive - should not happen in production)
            return new CombatProfile(playerId, DefaultStrength, DefaultStamina);
        }

        _logger.LogDebug(
            "Retrieved combat profile for PlayerId: {PlayerId} (Strength: {Strength}, Stamina: {Stamina}, Version: {Version})",
            playerId, profile.Strength, profile.Stamina, profile.Version);

        return new CombatProfile(profile.PlayerId, profile.Strength, profile.Stamina);
    }
}


