namespace MkvProcessor.Models;

/// <summary>
/// Represents a TV show from TVDB
/// </summary>
public class TvShow
{
    /// <summary>TVDB show ID</summary>
    public int Id { get; init; }

    /// <summary>Show name</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Original air date year</summary>
    public int? Year { get; init; }

    /// <summary>Show status (Continuing, Ended, etc.)</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Poster image URL</summary>
    public string? PosterUrl { get; init; }

    /// <summary>Network/streaming service name</summary>
    public string? Network { get; init; }

    /// <summary>Show overview/description</summary>
    public string? Overview { get; init; }

    /// <summary>Seasons list (populated when fetching full show data)</summary>
    public List<Season> Seasons { get; set; } = [];

    /// <summary>Timestamp when this data was cached</summary>
    public DateTime CachedAt { get; set; }

    /// <summary>Display string for UI (Name with year)</summary>
    public string DisplayName => Year.HasValue ? $"{Name} ({Year})" : Name;
}
