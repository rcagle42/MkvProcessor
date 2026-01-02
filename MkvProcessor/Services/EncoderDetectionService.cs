using System.Diagnostics;
using MkvProcessor.Models;

namespace MkvProcessor.Services;

/// <summary>
/// Service for detecting available hardware video encoders
/// </summary>
public class EncoderDetectionService
{
    /// <summary>
    /// Detects all available encoders (CPU is always available)
    /// </summary>
    public async Task<List<EncoderInfo>> DetectAvailableEncodersAsync()
    {
        var encoders = new List<EncoderInfo>
        {
            EncoderInfo.Cpu()
        };

        // Check hardware encoders in parallel
        var nvencTask = TestEncoderAsync("h264_nvenc");
        var qsvTask = TestEncoderAsync("h264_qsv");
        var amfTask = TestEncoderAsync("h264_amf");

        await Task.WhenAll(nvencTask, qsvTask, amfTask);

        if (await nvencTask)
            encoders.Add(EncoderInfo.Nvenc(true));

        if (await qsvTask)
            encoders.Add(EncoderInfo.Qsv(true));

        if (await amfTask)
            encoders.Add(EncoderInfo.Amf(true));

        return encoders;
    }

    /// <summary>
    /// Tests if a specific encoder is available and functional
    /// </summary>
    private async Task<bool> TestEncoderAsync(string codecName)
    {
        try
        {
            // First check if encoder is listed
            var listed = await IsEncoderListedAsync(codecName);
            if (!listed)
                return false;

            // Then test if it actually works by doing a minimal encode
            return await TestEncoderWorksAsync(codecName);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if an encoder is listed in FFmpeg's encoder list
    /// </summary>
    private async Task<bool> IsEncoderListedAsync(string codecName)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = FFmpegLocator.FFmpegPath,
            Arguments = "-hide_banner -encoders",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process == null)
            return false;

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return output.Contains(codecName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tests if an encoder actually works by performing a minimal encode
    /// </summary>
    private async Task<bool> TestEncoderWorksAsync(string codecName)
    {
        var arguments = codecName switch
        {
            "h264_nvenc" => "-hide_banner -loglevel error -f lavfi -i nullsrc=s=256x256:d=0.1 -c:v h264_nvenc -f null NUL",
            "h264_qsv" => "-hide_banner -loglevel error -f lavfi -i nullsrc=s=256x256:d=0.1 -c:v h264_qsv -f null NUL",
            "h264_amf" => "-hide_banner -loglevel error -f lavfi -i nullsrc=s=256x256:d=0.1 -c:v h264_amf -f null NUL",
            _ => throw new ArgumentException($"Unknown codec: {codecName}")
        };

        var startInfo = new ProcessStartInfo
        {
            FileName = FFmpegLocator.FFmpegPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process == null)
            return false;

        await process.WaitForExitAsync();
        return process.ExitCode == 0;
    }

    /// <summary>
    /// Gets encoder arguments for FFmpeg based on encoder type and quality settings
    /// </summary>
    public static string[] GetEncoderArguments(EncoderType encoder, QualityPreset quality)
    {
        return encoder switch
        {
            EncoderType.Nvenc => GetNvencArguments(quality),
            EncoderType.Qsv => GetQsvArguments(quality),
            EncoderType.Amf => GetAmfArguments(quality),
            _ => GetCpuArguments(quality)
        };
    }

    private static string[] GetCpuArguments(QualityPreset quality)
    {
        return ["-c:v", "libx264", "-preset", quality.Preset, "-crf", quality.Crf.ToString()];
    }

    private static string[] GetNvencArguments(QualityPreset quality)
    {
        // Map CPU presets to NVENC presets (p1-p7, higher = slower/better)
        var nvencPreset = quality.Preset switch
        {
            "slower" => "p7",
            "slow" => "p5",
            "medium" => "p4",
            "fast" => "p3",
            _ => "p4"
        };

        return ["-c:v", "h264_nvenc", "-preset", nvencPreset, "-cq", quality.Crf.ToString(), "-rc", "vbr"];
    }

    private static string[] GetQsvArguments(QualityPreset quality)
    {
        return ["-c:v", "h264_qsv", "-preset", quality.Preset, "-global_quality", quality.Crf.ToString()];
    }

    private static string[] GetAmfArguments(QualityPreset quality)
    {
        // Map CPU presets to AMF quality
        var amfQuality = quality.Preset switch
        {
            "slower" or "slow" => "quality",
            "medium" => "balanced",
            _ => "speed"
        };

        return ["-c:v", "h264_amf", "-quality", amfQuality, "-rc", "cqp", "-qp_i", quality.Crf.ToString(), "-qp_p", quality.Crf.ToString()];
    }
}
