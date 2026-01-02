using System.IO;

namespace MkvProcessor.Models;

/// <summary>
/// User settings for processing configuration
/// </summary>
public class ProcessingSettings
{
    /// <summary>
    /// Content type (TV show or Movie)
    /// </summary>
    public ContentType ContentType { get; set; } = ContentType.Movie;

    /// <summary>
    /// Quality preset index (0-based)
    /// </summary>
    public int QualityPresetIndex { get; set; } = 1;

    /// <summary>
    /// Audio processing mode
    /// </summary>
    public AudioMode AudioMode { get; set; } = AudioMode.Dual;

    /// <summary>
    /// Selected encoder type
    /// </summary>
    public EncoderType Encoder { get; set; } = EncoderType.Cpu;

    /// <summary>
    /// Custom output folder (null = use "processed" subfolder)
    /// </summary>
    public string? OutputFolder { get; set; }

    /// <summary>
    /// Whether to use a custom output folder
    /// </summary>
    public bool UseCustomOutputFolder { get; set; } = false;

    /// <summary>
    /// Minimize to system tray when closing
    /// </summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>
    /// Show notification when processing completes
    /// </summary>
    public bool ShowCompletionNotification { get; set; } = true;

    /// <summary>
    /// Last used input folder
    /// </summary>
    public string? LastInputFolder { get; set; }

    /// <summary>
    /// Gets the current quality preset based on content type and index
    /// </summary>
    public QualityPreset GetCurrentQualityPreset()
    {
        var presets = ContentType == ContentType.Movie
            ? QualityPreset.MoviePresets
            : QualityPreset.TvShowPresets;

        var index = Math.Clamp(QualityPresetIndex, 0, presets.Length - 1);
        return presets[index];
    }

    /// <summary>
    /// Gets the output folder for a given input folder
    /// </summary>
    public string GetOutputFolder(string inputFolder)
    {
        if (UseCustomOutputFolder && !string.IsNullOrEmpty(OutputFolder))
        {
            return OutputFolder;
        }
        return Path.Combine(inputFolder, "processed");
    }
}
