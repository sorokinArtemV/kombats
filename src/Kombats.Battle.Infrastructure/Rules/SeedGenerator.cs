using System.Security.Cryptography;
using Kombats.Battle.Application.Abstractions;

namespace Kombats.Battle.Infrastructure.Rules;

/// <summary>
/// Infrastructure implementation of ISeedGenerator.
/// Uses cryptographically safe random number generation.
/// </summary>
public class SeedGenerator : ISeedGenerator
{
    public int GenerateSeed()
    {
        // Use RandomNumberGenerator for cryptographically safe randomness
        var bytes = new byte[4];
        RandomNumberGenerator.Fill(bytes);
        return BitConverter.ToInt32(bytes);
    }
}

