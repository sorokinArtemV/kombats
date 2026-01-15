namespace Kombats.Battle.Domain.Rules;

/// <summary>
/// Deterministic random number provider using a stable PRNG algorithm.
/// Uses splitmix64 for seed mixing and xoshiro256** for generation.
/// Fully deterministic: same seed always produces same sequence.
/// Thread-safe: not required, as each instance is used per turn/attack.
/// </summary>
public sealed class DeterministicRandomProvider : IRandomProvider
{
    private ulong _s0, _s1, _s2, _s3;

    /// <summary>
    /// Creates a deterministic random provider from a 64-bit seed.
    /// </summary>
    public DeterministicRandomProvider(ulong seed)
    {
        // Use splitmix64 to initialize state from seed
        // This ensures good distribution even for sequential seeds
        _s0 = SplitMix64(ref seed);
        _s1 = SplitMix64(ref seed);
        _s2 = SplitMix64(ref seed);
        _s3 = SplitMix64(ref seed);

        // Safeguard: ensure state is never all-zero (xoshiro256** requires at least one non-zero value)
        // This is extremely unlikely with splitmix64, but we guard against it for safety
        if ((_s0 | _s1 | _s2 | _s3) == 0)
        {
            _s0 = 1;
        }
    }

    /// <summary>
    /// Creates a deterministic random provider from a 32-bit seed (int).
    /// </summary>
    public DeterministicRandomProvider(int seed) : this((ulong)(uint)seed)
    {
    }

    /// <summary>
    /// Returns a random decimal value in the range [minInclusive, maxExclusive).
    /// Uses xoshiro256** algorithm for generation.
    /// 
    /// Implementation details:
    /// - Generates normalized value in [0, 1) using integer arithmetic (32-bit precision, 2^32 steps)
    /// - Scales to requested range: min + normalized * (max - min)
    /// - Clamps result to [minInclusive, maxInclusive] to handle edge cases
    /// 
    /// Precision: Uses 32-bit integer normalization, providing 2^32 discrete steps. This precision
    /// is sufficient for crit/dodge/damage rolls in combat resolution where determinism and stability
    /// are more important than perfect statistical distribution.
    /// 
    /// Range: Results are effectively in [minInclusive, maxExclusive) due to normalization from [0, 1),
    /// but clamping ensures the result never exceeds maxInclusive. In practice, maxInclusive is extremely
    /// unlikely to be reached (would require normalized value exactly 1.0, which is not possible from [0, 1)).
    /// </summary>
    public decimal NextDecimal(decimal minInclusive, decimal maxInclusive)
    {
        if (minInclusive > maxInclusive)
        {
            throw new ArgumentException("minInclusive must be less than or equal to maxInclusive");
        }

        if (minInclusive == maxInclusive)
        {
            return minInclusive;
        }

        // Generate next random value using xoshiro256**
        var randomValue = NextUInt64();

        // Convert to normalized decimal in [0, 1) using integer arithmetic
        // Use high 32 bits for good precision: 2^32 discrete steps is sufficient for crit/dodge/damage rolls
        var normalizedUint = (uint)(randomValue >> 32);
        var maxUint = (decimal)uint.MaxValue;
        var normalizedDecimal = normalizedUint / (maxUint + 1m); // [0, 1)

        // Scale to requested range: min + normalized * (max - min)
        var range = maxInclusive - minInclusive;
        var result = minInclusive + normalizedDecimal * range;

        // Clamp to ensure result is within [minInclusive, maxInclusive] (handles edge cases)
        if (result < minInclusive) return minInclusive;
        if (result > maxInclusive) return maxInclusive;
        return result;
    }

    /// <summary>
    /// Generates next 64-bit random value using xoshiro256** algorithm.
    /// </summary>
    private ulong NextUInt64()
    {
        var result = RotateLeft(_s1 * 5, 7) * 9;
        var t = _s1 << 17;

        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;

        _s2 ^= t;
        _s3 = RotateLeft(_s3, 45);

        return result;
    }

    /// <summary>
    /// SplitMix64 algorithm for seed mixing.
    /// </summary>
    private static ulong SplitMix64(ref ulong x)
    {
        x += 0x9e3779b97f4a7c15UL;
        var z = x;
        z = (z ^ (z >> 30)) * 0xbf58476d1ce4e5b9UL;
        z = (z ^ (z >> 27)) * 0x94d049bb133111ebUL;
        return z ^ (z >> 31);
    }

    /// <summary>
    /// Rotates a 64-bit value left by specified number of bits.
    /// </summary>
    private static ulong RotateLeft(ulong x, int k)
    {
        return (x << k) | (x >> (64 - k));
    }
}

