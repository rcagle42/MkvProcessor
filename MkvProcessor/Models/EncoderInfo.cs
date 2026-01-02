namespace MkvProcessor.Models;

/// <summary>
/// Information about an available video encoder
/// </summary>
public class EncoderInfo
{
    /// <summary>
    /// Encoder type
    /// </summary>
    public EncoderType Type { get; init; }

    /// <summary>
    /// FFmpeg codec name (e.g., "libx264", "h264_nvenc")
    /// </summary>
    public string CodecName { get; init; } = string.Empty;

    /// <summary>
    /// Display name for UI (e.g., "NVIDIA NVENC (h264_nvenc)")
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Whether this encoder is available and functional
    /// </summary>
    public bool IsAvailable { get; init; }

    /// <summary>
    /// Whether this is a hardware encoder
    /// </summary>
    public bool IsHardwareEncoder => Type != EncoderType.Cpu;

    /// <summary>
    /// Creates encoder info for CPU (libx264)
    /// </summary>
    public static EncoderInfo Cpu() => new()
    {
        Type = EncoderType.Cpu,
        CodecName = "libx264",
        DisplayName = "CPU (libx264)",
        IsAvailable = true
    };

    /// <summary>
    /// Creates encoder info for NVIDIA NVENC
    /// </summary>
    public static EncoderInfo Nvenc(bool available) => new()
    {
        Type = EncoderType.Nvenc,
        CodecName = "h264_nvenc",
        DisplayName = "NVIDIA NVENC (h264_nvenc)",
        IsAvailable = available
    };

    /// <summary>
    /// Creates encoder info for Intel QuickSync
    /// </summary>
    public static EncoderInfo Qsv(bool available) => new()
    {
        Type = EncoderType.Qsv,
        CodecName = "h264_qsv",
        DisplayName = "Intel QuickSync (h264_qsv)",
        IsAvailable = available
    };

    /// <summary>
    /// Creates encoder info for AMD AMF
    /// </summary>
    public static EncoderInfo Amf(bool available) => new()
    {
        Type = EncoderType.Amf,
        CodecName = "h264_amf",
        DisplayName = "AMD AMF (h264_amf)",
        IsAvailable = available
    };
}
