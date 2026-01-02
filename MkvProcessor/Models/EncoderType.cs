namespace MkvProcessor.Models;

/// <summary>
/// Available video encoder types
/// </summary>
public enum EncoderType
{
    /// <summary>
    /// Software encoding with libx264
    /// </summary>
    Cpu,

    /// <summary>
    /// NVIDIA NVENC hardware encoding
    /// </summary>
    Nvenc,

    /// <summary>
    /// Intel QuickSync hardware encoding
    /// </summary>
    Qsv,

    /// <summary>
    /// AMD AMF hardware encoding
    /// </summary>
    Amf
}
