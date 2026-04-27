using System.Diagnostics;
using System.IO;
using MkvProcessor.Models;

namespace MkvProcessor.Services.Subtitle.Strategies;

/// <summary>
/// Wraps the existing <see cref="PgsToSrtService"/> as a strategy so it can participate in the
/// orchestration chain. Handles both MKV-stream sources (extracts the .sup via mkvextract or
/// FFmpeg first) and standalone .sup files from the Subtitle Converter tab. Applies only to
/// PGS bitmap subtitles — VobSub/DVB routing happens in other strategies.
/// </summary>
public class PgsToSrtStrategy : ISubtitleExtractionStrategy
{
    private readonly PgsToSrtService _pgsToSrt;
    private readonly MkvExtractService _mkvExtract;

    public PgsToSrtStrategy(PgsToSrtService pgsToSrt, MkvExtractService mkvExtract)
    {
        _pgsToSrt = pgsToSrt;
        _mkvExtract = mkvExtract;
    }

    public string Name => "PgsToSrt";

    public bool IsAvailable => _pgsToSrt.IsAvailable;

    public bool CanHandle(SubtitleSourceDescriptor source) =>
        source.CodecClass == SubtitleCodecClass.PgsBitmap;

    public async Task<SubtitleStrategyResult> RunAsync(
        SubtitleStrategyRequest request,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var logs = new List<string>();
        var source = request.Source;

        Directory.CreateDirectory(request.OutputDirectory);

        // Step 1: obtain a .sup file on disk. Standalone sources already have one; MKV sources
        // need to be demuxed first. Extracted .sup files are treated as temp and deleted after.
        string supPath;
        bool supIsTemp;

        if (source.Kind == SubtitleSourceKind.StandaloneFile)
        {
            supPath = source.SourcePath;
            supIsTemp = false;
        }
        else
        {
            supPath = Path.Combine(
                request.OutputDirectory,
                $"{request.OutputBaseName}.pgstosrt.sup");
            supIsTemp = true;

            var extracted = await ExtractSupAsync(source, supPath, logs, cancellationToken);
            if (!extracted)
            {
                sw.Stop();
                return SubtitleStrategyResult.Failure("Failed to extract .sup from MKV", sw.Elapsed, logs);
            }
        }

        // Step 2: build a SubtitleFile wrapper and invoke the existing service. Its output
        // goes next to the input .sup with the same base name + .srt — which, when we extracted
        // to {baseName}.pgstosrt.sup, yields {baseName}.pgstosrt.srt in the requested directory.
        var wrapper = new SubtitleFile
        {
            FilePath = supPath,
            FileName = Path.GetFileName(supPath),
        };

        void ForwardLog(string line) => logs.Add(line);
        _pgsToSrt.LogOutput += ForwardLog;

        SubtitleConversionResult serviceResult;
        try
        {
            serviceResult = await _pgsToSrt.ConvertAsync(
                wrapper, request.Language, request.TessdataPath, cancellationToken);
        }
        finally
        {
            _pgsToSrt.LogOutput -= ForwardLog;
            if (supIsTemp)
            {
                try { if (File.Exists(supPath)) File.Delete(supPath); } catch { }
            }
        }

        sw.Stop();

        if (!serviceResult.Success || string.IsNullOrEmpty(serviceResult.OutputPath))
            return SubtitleStrategyResult.Failure(
                serviceResult.ErrorMessage ?? "PgsToSrt conversion failed", sw.Elapsed, logs);

        return new SubtitleStrategyResult(true, serviceResult.OutputPath, null, sw.Elapsed, logs);
    }

    /// <summary>
    /// Extracts a PGS track from an MKV file to the given .sup path. Prefers mkvextract for
    /// its reliability on damaged or unusual tracks; falls back to FFmpeg stream copy.
    /// </summary>
    private async Task<bool> ExtractSupAsync(
        SubtitleSourceDescriptor source,
        string supPath,
        List<string> logs,
        CancellationToken cancellationToken)
    {
        if (_mkvExtract.IsAvailable)
        {
            logs.Add($"[mkvextract] extracting PGS track {source.StreamIndex} → {Path.GetFileName(supPath)}");
            var result = await _mkvExtract.ExtractTrackAsync(
                source.SourcePath, source.StreamIndex, supPath, cancellationToken);

            if (result.Success)
                return true;

            logs.Add($"mkvextract failed: {result.ErrorMessage}. Falling back to FFmpeg.");
        }

        return await RunFFmpegCopyAsync(source, supPath, logs, cancellationToken);
    }

    private async Task<bool> RunFFmpegCopyAsync(
        SubtitleSourceDescriptor source,
        string supPath,
        List<string> logs,
        CancellationToken cancellationToken)
    {
        var args = $"-hide_banner -loglevel error -y -i \"{source.SourcePath}\" " +
                   $"-map 0:{source.StreamIndex} -c:s copy \"{supPath}\"";

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

        if (process.ExitCode != 0)
            return false;

        return File.Exists(supPath) && new FileInfo(supPath).Length > 0;
    }
}
