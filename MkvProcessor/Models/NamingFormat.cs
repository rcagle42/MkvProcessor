namespace MkvProcessor.Models;

/// <summary>
/// Episode naming format styles for Plex compatibility
/// </summary>
public enum NamingFormat
{
    /// <summary>Format: Show Name - 01x01 - Episode Name</summary>
    Standard,

    /// <summary>Format: Show Name - S01E01 - Episode Name</summary>
    Scene
}
