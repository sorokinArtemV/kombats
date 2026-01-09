using Combats.Battle.Domain.Rules;

namespace Combats.Battle.Infrastructure.Rules;

/// <summary>
/// Infrastructure implementation of IRandomProvider using System.Random.Shared.
/// </summary>
public class SystemRandomProvider : IRandomProvider
{
    public decimal NextDecimal(decimal minInclusive, decimal maxInclusive)
    {
        if (minInclusive > maxInclusive)
            throw new ArgumentException("minInclusive must be less than or equal to maxInclusive");

        if (minInclusive == maxInclusive)
            return minInclusive;

        // Use Random.Shared for thread-safe random generation
        var range = (double)(maxInclusive - minInclusive);
        var randomValue = Random.Shared.NextDouble() * range;
        return minInclusive + (decimal)randomValue;
    }
}

