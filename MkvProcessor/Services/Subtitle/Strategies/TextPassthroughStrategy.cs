using System.Diagnostics;
using System.IO;
using System.Text;

namespace MkvProcessor.Services.Subtitle.Strategies;

/// <summary>
/// Extracts text-based subtitle tracks (subrip, ass, mov_text, webvtt, etc.) from an MKV and
/// writes them as SRT. Prefers mkvextract for native subrip tracks (byte-for-byte passthrough),
/// falls back to FFmpeg for codecs that need conversion into SRT. Standalone files are not
/// handled here — they would already be on disk as text.
/// </summary>
public class TextPassthroughStrategy : ISubtitleExtractionStrategy
{
    private readonly MkvExtractService _mkvExtract;

    public TextPassthroughStrategy(MkvExtractService mkvExtract)
    {
        _mkvExtract = mkvExtract;
    }

    public string Name => "Text Passthrough";

    public bool IsAvailable => FFmpegLocator.IsFFmpegAvailable; // mkvextract is optional boost

    public bool CanHandle(SubtitleSourceDescriptor source) =>
        source.Kind == SubtitleSourceKind.MkvStream &&
        source.CodecClass == SubtitleCodecClass.Text;

    public async Task<SubtitleStrategyResult> RunAsync(
        SubtitleStrategyRequest request,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var source = request.Source;
        var outputPath = Path.Combine(
            request.OutputDirectory,
            $"{request.OutputBaseName}.textpassthrough.srt");

        Directory.CreateDirectory(request.OutputDirectory);

        var logs = new List<string>();
        var codec = source.CodecName.ToLowerInvariant();
        var isNativeSrt = codec.Contains("subrip") || codec == "srt";

        // Prefer mkvextract for native subrip — zero-conversion passthrough.
        if (isNativeSrt && _mkvExtract.IsAvailable)
        {
            logs.Add($"[mkvextract] extracting track {source.StreamIndex} → {Path.GetFileName(outputPath)}");
            var extractResult = await _mkvExtract.ExtractTrackAsync(
                source.SourcePath, source.StreamIndex, outputPath, cancellationToken);

            sw.Stop();
            if (extractResult.Success)
                return new SubtitleStrategyResult(true, outputPath, null, sw.Elapsed, logs);

            logs.Add($"mkvextract failed: {extractResult.ErrorMessage}. Falling back to FFmpeg.");
            sw.Restart();
        }

        // FFmpeg path: convert any text codec to SRT.
        var ffResult = await RunFFmpegConvertAsync(source, outputPath, logs, cancellationToken);
        sw.Stop();

        if (!ffResult)
            return SubtitleStrategyResult.Failure("FFmpeg text extraction failed", sw.Elapsed, logs);

        if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
            return SubtitleStrategyResult.Failure("FFmpeg produced empty output", sw.Elapsed, logs);

        return new SubtitleStrategyResult(true, outputPath, null, sw.Elapsed, logs);
    }

    private async Task<bool> RunFFmpegConvertAsync(
        SubtitleSourceDescriptor source,
        string outputPath,
        List<string> logs,
        CancellationToken cancellationToken)
    {
        var args = $"-hide_banner -loglevel error -y -i \"{source.SourcePath}\" " +
                   $"-map 0:{source.StreamIndex} -c:s srt \"{outputPath}\"";

        var psi = new ProcessStartInfo
        {
            FileName = FFmpegLocator.FFmpegPath,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process is null)
        {
            logs.Add("Failed to start ffmpeg");
            return false;
        }

        try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }

        using var _ = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
        });

        var stderrTask = Task.Run(async () =>
        {
            string? line;
            while ((line = await process.StandardError.ReadLineAsync(cancellationToken)) != null)
                logs.Add(line);
        }, cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        await stderrTask;

        return process.ExitCode == 0;
    }
}
