using System.IO;

namespace MkvProcessor.Models;

/// <summary>
/// User settings for processing configuration
/// </summary>
public class ProcessingSettings
{
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

    // === TV Renamer Settings ===

    /// <summary>
    /// TVDB API key for TV show lookups
    /// </summary>
    public string? TvdbApiKey { get; set; }

    /// <summary>
    /// TVDB subscriber PIN (optional, only needed for user-supported keys)
    /// </summary>
    public string? TvdbPin { get; set; }

    /// <summary>
    /// Preferred episode naming format (default: Standard 01x01)
    /// </summary>
    public NamingFormat EpisodeNamingFormat { get; set; } = NamingFormat.Standard;

    /// <summary>
    /// Last folder used for TV file renaming
    /// </summary>
    public string? LastTvRenamerFolder { get; set; }

    // === Subtitle Converter Settings ===

    /// <summary>
    /// Path to PgsToSrt.dll (null = auto-detect)
    /// </summary>
    public string? PgsToSrtPath { get; set; }

    /// <summary>
    /// Path to Tesseract traineddata files
    /// </summary>
    public string? TessdataPath { get; set; }

    /// <summary>
    /// Default language for OCR (e.g., "eng", "spa", "fra")
    /// </summary>
    public string DefaultSubtitleLanguage { get; set; } = "eng";

    /// <summary>
    /// Last folder used for subtitle conversion
    /// </summary>
    public string? LastSubtitleFolder { get; set; }

    /// <summary>
    /// Gets the current quality preset based on index
    /// </summary>
    public QualityPreset GetCurrentQualityPreset()
    {
        var presets = QualityPreset.Presets;
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
