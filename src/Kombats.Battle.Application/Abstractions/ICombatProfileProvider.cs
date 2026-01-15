namespace Kombats.Battle.Application.Abstractions;

/// <summary>
/// Port interface for retrieving player combat profiles (stats).
/// Application requests player stats via this interface.
/// Infrastructure provides implementation (temporary defaults or DB/projection).
/// </summary>
public interface ICombatProfileProvider
{
    /// <summary>
    /// Gets combat profile (stats) for a player.
    /// Returns null if player not found (should not happen in normal flow).
    /// </summary>
    Task<CombatProfile?> GetProfileAsync(Guid playerId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Player combat profile (stats snapshot).
/// </summary>
public record CombatProfile(
    Guid PlayerId,
    int Strength,
    int Stamina,
    int Agility,
    int Intuition);





