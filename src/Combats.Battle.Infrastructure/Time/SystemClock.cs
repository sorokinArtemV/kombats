using Combats.Battle.Application.Abstractions;

namespace Combats.Battle.Infrastructure.Time;

/// <summary>
/// System clock implementation of IClock.
/// Uses DateTime.UtcNow.
/// </summary>
public class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}





