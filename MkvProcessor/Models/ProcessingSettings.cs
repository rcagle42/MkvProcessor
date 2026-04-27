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
    /// Last used input folder
    /// </summary>
    public string? LastInputFolder { get; set; }

    /// <summary>
    /// Language filter for subtitle extraction (e.g., "eng", "spa", or "all" for all languages)
    /// </summary>
    public string SubtitleLanguageFilter { get; set; } = "eng";

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

    // === Subtitle Orchestrator Settings ===

    /// <summary>
    /// Path to mkvextract.exe (null = auto-detect in bundled /mkvtoolnix/ folder or PATH)
    /// </summary>
    public string? MkvExtractPath { get; set; }

    /// <summary>
    /// Minimum orchestrator score (0-100) for a candidate to be accepted without running
    /// further strategies. Lower = faster, higher = more thorough.
    /// </summary>
    public int SubtitleMinAcceptableScore { get; set; } = 60;

    /// <summary>
    /// When true, the Processing pipeline routes subtitle extraction through the orchestrator.
    /// When false, falls back to the legacy FFmpeg-direct extraction path.
    /// </summary>
    public bool EnableOrchestratorInProcessing { get; set; } = true;

    /// <summary>
    /// When true, the Subtitle Converter tab runs every applicable strategy and shows results
    /// side-by-side in the Advanced/Compare panel instead of short-circuiting on the first hit.
    /// </summary>
    public bool SubtitleCompareMode { get; set; } = false;

    /// <summary>
    /// Gets the current quality preset based on index
    /// </summary>
    public QualityPreset GetCurrentQualityPreset()
    {
        var presets = QualityPreset.Presets;
        var index = Math.Clamp(QualityPresetIndex, 0, presets.Length - 1);
        return presets[index];
    }
}
