using Kombats.Battle.Application.Abstractions;
using Kombats.Battle.Infrastructure.Data.DbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kombats.Battle.Infrastructure.Profiles;

/// <summary>
/// Database implementation of ICombatProfileProvider.
/// Reads player combat profiles from PostgreSQL read model.
/// Falls back to defaults if profile not found (defensive).
/// </summary>
public class DatabaseCombatProfileProvider : ICombatProfileProvider
{
    private readonly BattleDbContext _dbContext;
    private readonly ILogger<DatabaseCombatProfileProvider> _logger;
    private const int DefaultStrength = 3;
    private const int DefaultStamina = 3;
    private const int DefaultAgility = 3;
    private const int DefaultIntuition = 3;

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
                "Player profile not found for PlayerId: {PlayerId}. Using defaults (Strength: {Strength}, Stamina: {Stamina}, Agility: {Agility}, Intuition: {Intuition}). " +
                "Consider creating a projection consumer to populate player_profiles table from character service events.",
                playerId, DefaultStrength, DefaultStamina, DefaultAgility, DefaultIntuition);

            // Fallback to defaults (defensive - should not happen in production)
            return new CombatProfile(playerId, DefaultStrength, DefaultStamina, DefaultAgility, DefaultIntuition);
        }

        // Use 0 as default for Agility and Intuition if not in DB (backward compatibility)
        var agility = profile.Agility;
        var intuition = profile.Intuition;

        _logger.LogDebug(
            "Retrieved combat profile for PlayerId: {PlayerId} (Strength: {Strength}, Stamina: {Stamina}, Agility: {Agility}, Intuition: {Intuition}, Version: {Version})",
            playerId, profile.Strength, profile.Stamina, agility, intuition, profile.Version);

        return new CombatProfile(profile.PlayerId, profile.Strength, profile.Stamina, agility, intuition);
    }
}