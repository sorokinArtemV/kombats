namespace Kombats.Battle.Application.UseCases.Turns;

/// <summary>
/// Pure predicate that decides whether a turn deadline has passed enough to resolve.
/// Kept in Application layer for easy unit testing.
/// </summary>
public static class TurnDeadlinePolicy
{
    /// <summary>
    /// Only resolve when 'now' is after 'deadlineUtc' plus a small skew buffer.
    /// </summary>
    public static bool ShouldResolve(DateTimeOffset now, DateTimeOffset deadlineUtc, int skewMs)
    {
        return now >= deadlineUtc.AddMilliseconds(skewMs);
    }
}




