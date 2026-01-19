namespace Kombats.Matchmaking.Application.Options;

/// <summary>
/// Configuration options for queue operations.
/// </summary>
public class QueueOptions
{
    public const string SectionName = "Matchmaking:Queue";

    /// <summary>
    /// Default variant to use when variant is not specified.
    /// Default: "default".
    /// </summary>
    public string DefaultVariant { get; set; } = "default";
}

