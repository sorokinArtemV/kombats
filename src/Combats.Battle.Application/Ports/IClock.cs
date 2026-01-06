namespace Combats.Battle.Application.Ports;

/// <summary>
/// Port interface for time abstraction.
/// Application uses this instead of DateTime.UtcNow for testability and deadline calculations.
/// </summary>
public interface IClock
{
    DateTime UtcNow { get; }
}



