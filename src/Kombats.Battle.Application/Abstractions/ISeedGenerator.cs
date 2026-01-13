namespace Kombats.Battle.Application.Abstractions;

/// <summary>
/// Port interface for generating battle seeds.
/// Application uses this to generate seeds without depending on Infrastructure implementation details.
/// </summary>
public interface ISeedGenerator
{
    /// <summary>
    /// Generates a cryptographically safe random seed for a battle.
    /// </summary>
    /// <returns>A random integer seed.</returns>
    int GenerateSeed();
}

