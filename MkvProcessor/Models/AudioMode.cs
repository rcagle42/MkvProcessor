namespace MkvProcessor.Models;

/// <summary>
/// Audio processing mode for output files
/// </summary>
public enum AudioMode
{
    /// <summary>
    /// Normalized stereo track + original surround track
    /// </summary>
    Dual,

    /// <summary>
    /// Keep only original audio tracks
    /// </summary>
    Original,

    /// <summary>
    /// Only normalized stereo track
    /// </summary>
    Normalized
}
