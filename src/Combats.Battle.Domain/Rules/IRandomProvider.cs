namespace Combats.Battle.Domain.Rules;

/// <summary>
/// Abstraction for random number generation.
/// Domain uses this interface to allow Infrastructure to provide implementation
/// and tests to use deterministic stubs.
/// </summary>
public interface IRandomProvider
{
    /// <summary>
    /// Returns a random decimal value in the range [minInclusive, maxInclusive].
    /// </summary>
    decimal NextDecimal(decimal minInclusive, decimal maxInclusive);
}

