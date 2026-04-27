using System.Diagnostics;
using System.IO;
using System.Text;

namespace MkvProcessor.Services.Subtitle;

/// <summary>
/// Thin wrapper over <c>mkvextract.exe</c>. Handles single-track extraction to a specified
/// output path. Unlike FFmpeg, mkvextract produces correct VobSub <c>.idx</c>+<c>.sub</c>
/// pairs and never silently writes empty files when a track is broken — it fails loudly.
/// </summary>
public class MkvExtractService
{
    public event Action<string>? LogOutput;

    public bool IsAvailable => MkvExtractLocator.IsAvailable;

    /// <summary>
    /// Extracts a single track from an MKV file to the given output path.
    /// For text/PGS tracks the extension on <paramref name="outputPath"/> determines the
    /// output format. For VobSub tracks (dvd_subtitle), mkvextract produces both a
    /// <c>.sub</c> and a sibling <c>.idx</c> — pass an <c>.idx</c> path and it will create
    /// both.
    /// </summary>
    public async Task<MkvExtractResult> ExtractTrackAsync(
        string mkvPath,
        int trackId,
        string outputPath,
        CancellationToken cancellationToken)
    {
        if (!IsAvailable)
            return new MkvExtractResult(false, "mkvextract.exe not found", string.Empty);

        if (!File.Exists(mkvPath))
            return new MkvExtractResult(false, $"Input MKV not found: {mkvPath}", string.Empty);

        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);

        // mkvextract syntax: mkvextract tracks <source.mkv> <trackid>:<output>
        var args = $"tracks \"{mkvPath}\" {trackId}:\"{outputPath}\"";

        var psi = new ProcessStartInfo
        {
            FileName = MkvExtractLocator.MkvExtractPath,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var log = new StringBuilder();
        using var process = Process.Start(psi);
        if (process is null)
            return new MkvExtractResult(false, "Failed to start mkvextract", string.Empty);

        try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }

        using var _ = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
        });

        var stdoutTask = Task.Run(async () =>
        {
            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync(cancellationToken)) != null)
            {
                log.AppendLine(line);
                LogOutput?.Invoke(line);
            }
        }, cancellationToken);

        var stderrTask = Task.Run(async () =>
        {
            string? line;
            while ((line = await process.StandardError.ReadLineAsync(cancellationToken)) != null)
            {
                log.AppendLine(line);
                LogOutput?.Invoke(line);
            }
        }, cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        await Task.WhenAll(stdoutTask, stderrTask);

        if (process.ExitCode != 0)
            return new MkvExtractResult(false, $"mkvextract exited with code {process.ExitCode}", log.ToString());

        if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
            return new MkvExtractResult(false, "mkvextract produced no output", log.ToString());

        return new MkvExtractResult(true, null, log.ToString());
    }
}

public sealed record MkvExtractResult(bool Success, string? ErrorMessage, string Log);
