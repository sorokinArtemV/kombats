using Kombats.Battle.Domain.Model;

namespace Kombats.Battle.Domain.Rules;

/// <summary>
/// Helper for creating deterministic RNG instances for battle turn resolution.
/// Encapsulates seed derivation logic to ensure order independence between A->B and B->A attacks.
/// 
/// Uses separate RNG streams (stream=1 for A->B, stream=2 for B->A) to guarantee that
/// the order of computation does not affect results. This ensures that resolving A->B first
/// then B->A produces the same outcomes as resolving B->A first then A->B.
/// </summary>
internal static class DeterministicTurnRng
{
    /// <summary>
    /// Creates two independent RNG instances for resolving attacks in a turn.
    /// The RNGs are derived from battle seed, battle ID, turn index, and player IDs,
    /// ensuring that A->B and B->A attacks use independent streams and results don't
    /// depend on computation order.
    /// 
    /// Seed derivation:
    /// 1. baseTurnSeed = Hash64(ruleset.Seed, battleId, turnIndex)
    /// 2. seedAtoB = Hash64(baseTurnSeed, playerAId, playerBId, stream=1)
    /// 3. seedBtoA = Hash64(baseTurnSeed, playerBId, playerAId, stream=2)
    /// 
    /// The stream parameter (1 vs 2) ensures A->B and B->A use completely independent RNG sequences.
    /// </summary>
    /// <param name="state">Current battle domain state</param>
    /// <returns>Tuple of (AtoB, BtoA) RNG providers for resolving attacks</returns>
    public static (IRandomProvider AtoB, IRandomProvider BtoA) Create(BattleDomainState state)
    {
        // Base seed for this turn: combines ruleset seed, battle ID, and turn index
        var baseTurnSeed = Hash64(
            (ulong)(uint)state.Ruleset.Seed,
            state.BattleId,
            (ulong)(uint)state.TurnIndex);

        // Derive separate seeds for each attack direction to ensure order independence
        // A->B uses stream=1, B->A uses stream=2
        // The stream parameter ensures independent RNG sequences regardless of computation order
        var seedAtoB = Hash64(baseTurnSeed, state.PlayerAId, state.PlayerBId, 1UL);
        var seedBtoA = Hash64(baseTurnSeed, state.PlayerBId, state.PlayerAId, 2UL);

        // Create RNG instances for each attack
        var rngAtoB = new DeterministicRandomProvider(seedAtoB);
        var rngBtoA = new DeterministicRandomProvider(seedBtoA);

        return (rngAtoB, rngBtoA);
    }

    /// <summary>
    /// Stable hash function for combining seed with a Guid and a ulong value.
    /// Uses splitmix64-style mixing for good distribution.
    /// This is NOT cryptographic, just a stable deterministic hash.
    /// </summary>
    private static ulong Hash64(ulong seed, Guid guid, ulong value)
    {
        var guidBytes = guid.ToByteArray();
        var guidUlong1 = BitConverter.ToUInt64(guidBytes, 0);
        var guidUlong2 = BitConverter.ToUInt64(guidBytes, 8);

        ulong h = seed;
        h ^= guidUlong1;
        h = Mix64(h);
        h ^= guidUlong2;
        h = Mix64(h);
        h ^= value;
        h = Mix64(h);
        return h;
    }

    /// <summary>
    /// Stable hash function for combining seed with two Guids and a stream identifier.
    /// Uses splitmix64-style mixing for good distribution.
    /// This is NOT cryptographic, just a stable deterministic hash.
    /// </summary>
    private static ulong Hash64(ulong seed, Guid guid1, Guid guid2, ulong stream)
    {
        var guid1Bytes = guid1.ToByteArray();
        var guid2Bytes = guid2.ToByteArray();
        var guid1Ulong1 = BitConverter.ToUInt64(guid1Bytes, 0);
        var guid1Ulong2 = BitConverter.ToUInt64(guid1Bytes, 8);
        var guid2Ulong1 = BitConverter.ToUInt64(guid2Bytes, 0);
        var guid2Ulong2 = BitConverter.ToUInt64(guid2Bytes, 8);

        ulong h = seed;
        h ^= guid1Ulong1;
        h = Mix64(h);
        h ^= guid1Ulong2;
        h = Mix64(h);
        h ^= guid2Ulong1;
        h = Mix64(h);
        h ^= guid2Ulong2;
        h = Mix64(h);
        h ^= stream;
        h = Mix64(h);
        return h;
    }

    /// <summary>
    /// Mix function based on splitmix64 for stable hashing.
    /// </summary>
    private static ulong Mix64(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xbf58476d1ce4e5b9UL;
        z = (z ^ (z >> 27)) * 0x94d049bb133111ebUL;
        return z ^ (z >> 31);
    }
}


