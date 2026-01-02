namespace MkvProcessor.Models;

/// <summary>
/// Represents a season of a TV show
/// </summary>
public class Season
{
    /// <summary>TVDB season ID</summary>
    public int Id { get; init; }

    /// <summary>Season number (0 for specials)</summary>
    public int Number { get; init; }

    /// <summary>Season name (if any)</summary>
    public string? Name { get; init; }

    /// <summary>Episodes in this season</summary>
    public List<Episode> Episodes { get; set; } = [];

    /// <summary>Number of episodes in this season</summary>
    public int EpisodeCount => Episodes.Count;

    /// <summary>Display string for UI</summary>
    public string DisplayName => Number == 0 ? "Specials" : $"Season {Number}";
}
