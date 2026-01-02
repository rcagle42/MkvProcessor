namespace MkvProcessor.Models;

/// <summary>
/// Represents an episode of a TV show
/// </summary>
public class Episode
{
    /// <summary>TVDB episode ID</summary>
    public int Id { get; init; }

    /// <summary>Episode name/title</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Season number</summary>
    public int SeasonNumber { get; init; }

    /// <summary>Episode number within the season</summary>
    public int EpisodeNumber { get; init; }

    /// <summary>Original air date</summary>
    public string? AiredDate { get; init; }

    /// <summary>Episode overview/description</summary>
    public string? Overview { get; init; }

    /// <summary>Runtime in minutes</summary>
    public int? Runtime { get; init; }

    /// <summary>Display string for UI (e.g., "01x05 - Episode Name")</summary>
    public string DisplayName => $"{SeasonNumber:D2}x{EpisodeNumber:D2} - {Name}";
}
