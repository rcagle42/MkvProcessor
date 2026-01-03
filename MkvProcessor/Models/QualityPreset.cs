namespace MkvProcessor.Models;

/// <summary>
/// Video encoding quality preset with CRF and encoder preset values
/// </summary>
public class QualityPreset
{
    /// <summary>
    /// Display name for the preset
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// CRF value (lower = better quality, larger file)
    /// </summary>
    public int Crf { get; init; }

    /// <summary>
    /// Encoder preset (e.g., "medium", "slow", "slower")
    /// </summary>
    public string Preset { get; init; } = "medium";

    /// <summary>
    /// Audio bitrate for this quality level
    /// </summary>
    public string AudioBitrate { get; init; } = "256k";

    /// <summary>
    /// Description shown in UI
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Gets available quality presets
    /// </summary>
    public static QualityPreset[] Presets =>
    [
        new() { Name = "Good", Crf = 22, Preset = "medium", AudioBitrate = "320k", Description = "CRF 22, Medium preset (faster)" },
        new() { Name = "High", Crf = 20, Preset = "medium", AudioBitrate = "320k", Description = "CRF 20, Medium preset (balanced)" },
        new() { Name = "Excellent", Crf = 18, Preset = "slow", AudioBitrate = "320k", Description = "CRF 18, Slow preset (best quality)" },
        new() { Name = "Reference", Crf = 16, Preset = "slower", AudioBitrate = "320k", Description = "CRF 16, Slower preset (near-lossless)" }
    ];
}
