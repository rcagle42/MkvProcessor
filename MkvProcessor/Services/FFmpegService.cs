using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using MkvProcessor.Models;

namespace MkvProcessor.Services;

/// <summary>
/// Core FFmpeg processing service for converting MKV to MP4
/// </summary>
public partial class FFmpegService
{
    private readonly MediaInfoService _mediaInfoService = new();

    /// <summary>
    /// Raised when FFmpeg outputs a log line
    /// </summary>
    public event Action<string>? LogOutput;

    /// <summary>
    /// Raised when progress changes (0-100)
    /// </summary>
    public event Action<double>? ProgressChanged;

    /// <summary>
    /// Raised when the current step changes
    /// </summary>
    public event Action<string>? StepChanged;

    [GeneratedRegex(@"time=(\d{2}):(\d{2}):(\d{2})\.(\d{2})")]
    private static partial Regex TimeRegex();

    /// <summary>
    /// Processes a single MKV file
    /// </summary>
    public async Task<ProcessingResult> ProcessFileAsync(
        MkvFile file,
        ProcessingSettings settings,
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.Now;
        var inputFolder = Path.GetDirectoryName(file.FilePath) ?? "";
        var outputFolder = settings.GetOutputFolder(inputFolder);
        var outputPath = Path.Combine(outputFolder, Path.ChangeExtension(file.FileName, ".mp4"));
        var tempAudioPath = Path.Combine(outputFolder, $"temp_{Guid.NewGuid():N}.m4a");

        LogOutput?.Invoke($"Processing: {file.FileName}");
        LogOutput?.Invoke($"  Input: {file.FilePath}");
        LogOutput?.Invoke($"  Output folder: {outputFolder}");
        LogOutput?.Invoke($"  Output path: {outputPath}");

        try
        {
            // Create output directory if needed
            Directory.CreateDirectory(outputFolder);
            LogOutput?.Invoke($"  Output directory created/verified");

            // Check if already processed
            if (File.Exists(outputPath))
            {
                LogOutput?.Invoke($"  SKIPPED: Output file already exists");
                file.Status = FileStatus.Skipped;
                return ProcessingResult.SkippedResult(file, outputPath);
            }

            LogOutput?.Invoke($"  Starting conversion...");

            file.Status = FileStatus.Processing;
            file.Progress = 0;

            // Step 1: Extract subtitles
            StepChanged?.Invoke("Extracting subtitles...");
            file.CurrentStep = "Extracting subtitles";
            await ExtractSubtitlesAsync(file.FilePath, outputFolder, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            // Step 2: Audio normalization (if needed)
            var hasNormalizedAudio = false;
            if (settings.AudioMode is AudioMode.Dual or AudioMode.Normalized)
            {
                StepChanged?.Invoke("Normalizing audio (two-pass)...");
                file.CurrentStep = "Normalizing audio";
                hasNormalizedAudio = await CreateNormalizedAudioAsync(
                    file.FilePath, tempAudioPath, settings.GetCurrentQualityPreset().AudioBitrate, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Step 3: Encode video
            StepChanged?.Invoke("Encoding video...");
            file.CurrentStep = "Encoding video";
            await EncodeVideoAsync(file, settings, outputPath, tempAudioPath, hasNormalizedAudio, cancellationToken);

            // Cleanup temp file
            if (File.Exists(tempAudioPath))
            {
                File.Delete(tempAudioPath);
            }

            file.Status = FileStatus.Complete;
            file.Progress = 100;
            file.OutputPath = outputPath;

            var outputInfo = new FileInfo(outputPath);
            return ProcessingResult.Successful(file, outputPath, outputInfo.Length, DateTime.Now - startTime);
        }
        catch (OperationCanceledException)
        {
            // Cleanup on cancellation
            LogOutput?.Invoke("Processing cancelled - cleaning up partial files...");
            await Task.Delay(500); // Brief delay to ensure FFmpeg has released file handles
            CleanupTempFiles(outputPath, tempAudioPath);
            file.Status = FileStatus.Pending;
            file.Progress = 0;
            file.CurrentStep = string.Empty;
            LogOutput?.Invoke("Cleanup complete");
            throw;
        }
        catch (Exception ex)
        {
            CleanupTempFiles(outputPath, tempAudioPath);
            file.Status = FileStatus.Error;
            file.ErrorMessage = ex.Message;
            return ProcessingResult.Failed(file, ex.Message, DateTime.Now - startTime);
        }
    }

    /// <summary>
    /// Extracts subtitles from the MKV file (non-fatal - continues on error)
    /// </summary>
    private async Task ExtractSubtitlesAsync(string inputPath, string outputFolder, CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileNameWithoutExtension(inputPath);

        try
        {
            // First, detect what subtitle streams exist and their types
            var subtitleStreams = await _mediaInfoService.GetSubtitleStreamsAsync(inputPath);

            if (subtitleStreams.Count == 0)
            {
                LogOutput?.Invoke("  No subtitle streams found");
                return;
            }

            LogOutput?.Invoke($"  Found {subtitleStreams.Count} subtitle stream(s)");

            // Separate text-based and bitmap-based subtitles
            var textSubtitles = subtitleStreams.Where(s => IsTextSubtitle(s.CodecName)).ToList();
            var bitmapSubtitles = subtitleStreams.Where(s => IsBitmapSubtitle(s.CodecName)).ToList();

            // Extract text subtitles as SRT (one file per stream)
            foreach (var sub in textSubtitles)
            {
                var streamIndex = subtitleStreams.IndexOf(sub);
                var srtPath = Path.Combine(outputFolder, $"{fileName}.{streamIndex}.srt");

                try
                {
                    var srtArgs = $"-hide_banner -loglevel error -y -i \"{inputPath}\" -map 0:s:{streamIndex} -c:s srt \"{srtPath}\"";
                    await RunFFmpegAsync(srtArgs, null, cancellationToken, suppressProgress: true, ignoreExitCode: true);

                    if (File.Exists(srtPath) && new FileInfo(srtPath).Length > 0)
                    {
                        LogOutput?.Invoke($"  Extracted text subtitle #{streamIndex} ({sub.CodecName}) as SRT");
                    }
                }
                catch (Exception ex)
                {
                    LogOutput?.Invoke($"  Warning: Could not extract text subtitle #{streamIndex}: {ex.Message}");
                }
            }

            // Extract bitmap subtitles in their native format
            foreach (var sub in bitmapSubtitles)
            {
                var streamIndex = subtitleStreams.IndexOf(sub);
                var extension = sub.IsPgs ? "sup" : "sub";
                var subPath = Path.Combine(outputFolder, $"{fileName}.{streamIndex}.{extension}");

                try
                {
                    var subArgs = $"-hide_banner -loglevel error -y -i \"{inputPath}\" -map 0:s:{streamIndex} -c:s copy \"{subPath}\"";
                    await RunFFmpegAsync(subArgs, null, cancellationToken, suppressProgress: true, ignoreExitCode: true);

                    if (File.Exists(subPath) && new FileInfo(subPath).Length > 0)
                    {
                        LogOutput?.Invoke($"  Extracted bitmap subtitle #{streamIndex} ({sub.CodecName}) as {extension.ToUpper()}");
                    }
                }
                catch (Exception ex)
                {
                    LogOutput?.Invoke($"  Warning: Could not extract bitmap subtitle #{streamIndex}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            // Subtitle extraction is non-fatal - just log and continue
            LogOutput?.Invoke($"  Warning: Subtitle extraction failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks if a subtitle codec is text-based (can be converted to SRT)
    /// </summary>
    private static bool IsTextSubtitle(string codecName)
    {
        var textCodecs = new[] { "subrip", "srt", "ass", "ssa", "mov_text", "webvtt", "text" };
        return textCodecs.Any(c => codecName.Contains(c, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Checks if a subtitle codec is bitmap-based (PGS, VobSub, DVB)
    /// </summary>
    private static bool IsBitmapSubtitle(string codecName)
    {
        var bitmapCodecs = new[] { "hdmv_pgs", "pgs", "dvd_subtitle", "dvdsub", "dvb_subtitle", "dvbsub", "xsub" };
        return bitmapCodecs.Any(c => codecName.Contains(c, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Creates a normalized stereo audio track using two-pass loudnorm
    /// </summary>
    private async Task<bool> CreateNormalizedAudioAsync(
        string inputPath, string outputPath, string bitrate, CancellationToken cancellationToken)
    {
        // Dialog-boosting pan matrix
        const string panMatrix = "pan=stereo|FL<FL+0.75*FC+0.5*BL+0.5*SL+0.5*LFE|FR<FR+0.75*FC+0.5*BR+0.5*SR+0.5*LFE";

        // Pass 1: Analyze audio levels
        LogOutput?.Invoke("Pass 1: Analyzing audio levels...");
        var analyzeArgs = $"-hide_banner -y -i \"{inputPath}\" -map 0:a:0 -af \"{panMatrix},loudnorm=I=-16:TP=-1.5:LRA=11:print_format=json\" -f null NUL";

        var analyzeOutput = await RunFFmpegAsync(analyzeArgs, null, cancellationToken, captureOutput: true, suppressProgress: true);

        // Parse loudnorm JSON output
        var loudnormStats = ParseLoudnormOutput(analyzeOutput);

        if (loudnormStats != null)
        {
            LogOutput?.Invoke($"Measured: I={loudnormStats.InputI} LUFS, TP={loudnormStats.InputTp} dB");

            // Pass 2: Apply normalization with measured values
            LogOutput?.Invoke("Pass 2: Applying normalization with dialog boost...");
            var loudnormFilter = $"loudnorm=I=-16:TP=-1.5:LRA=11:measured_I={loudnormStats.InputI}:measured_TP={loudnormStats.InputTp}:measured_LRA={loudnormStats.InputLra}:measured_thresh={loudnormStats.InputThresh}:offset={loudnormStats.TargetOffset}:linear=true";

            var encodeArgs = $"-hide_banner -stats -loglevel warning -y -i \"{inputPath}\" -map 0:a:0 -af \"{panMatrix},{loudnormFilter}\" -c:a aac -b:a {bitrate} \"{outputPath}\"";

            await RunFFmpegAsync(encodeArgs, null, cancellationToken, suppressProgress: true);
        }
        else
        {
            // Fallback to single-pass
            LogOutput?.Invoke("Warning: Using single-pass normalization");
            var fallbackArgs = $"-hide_banner -stats -loglevel warning -y -i \"{inputPath}\" -map 0:a:0 -af \"{panMatrix},loudnorm=I=-16:TP=-1.5:LRA=11\" -c:a aac -b:a {bitrate} \"{outputPath}\"";

            await RunFFmpegAsync(fallbackArgs, null, cancellationToken, suppressProgress: true);
        }

        return File.Exists(outputPath);
    }

    /// <summary>
    /// Encodes video with selected settings
    /// </summary>
    private async Task EncodeVideoAsync(
        MkvFile file,
        ProcessingSettings settings,
        string outputPath,
        string tempAudioPath,
        bool hasNormalizedAudio,
        CancellationToken cancellationToken)
    {
        var quality = settings.GetCurrentQualityPreset();
        var encoderArgs = EncoderDetectionService.GetEncoderArguments(settings.Encoder, quality);

        var args = new List<string>
        {
            "-hide_banner", "-stats", "-loglevel", "warning", "-y",
            "-i", $"\"{file.FilePath}\""
        };

        // Add temp audio input if we have normalized audio
        if (hasNormalizedAudio && File.Exists(tempAudioPath))
        {
            args.AddRange(["-i", $"\"{tempAudioPath}\""]);
        }

        // Map video
        args.AddRange(["-map", "0:v:0"]);

        // Add encoder arguments
        args.AddRange(encoderArgs);

        // Configure audio based on mode
        ConfigureAudioArguments(args, settings.AudioMode, hasNormalizedAudio, file.NeedsAudioTranscode, quality.AudioBitrate);

        // Output settings
        args.AddRange(["-movflags", "+faststart", $"\"{outputPath}\""]);

        var argsString = string.Join(" ", args);
        await RunFFmpegAsync(argsString, file.Duration, cancellationToken);
    }

    /// <summary>
    /// Configures audio mapping arguments based on audio mode
    /// </summary>
    private static void ConfigureAudioArguments(
        List<string> args, AudioMode mode, bool hasNormalizedAudio, bool needsTranscode, string audioBitrate)
    {
        switch (mode)
        {
            case AudioMode.Dual when hasNormalizedAudio:
                args.AddRange(["-map", "1:a:0"]); // Normalized audio first
                if (needsTranscode)
                {
                    args.AddRange([
                        "-map", "0:a:0",
                        "-c:a:0", "copy",
                        "-c:a:1", "aac", "-b:a:1", "448k"
                    ]);
                }
                else
                {
                    args.AddRange(["-map", "0:a:0", "-c:a", "copy"]);
                }
                args.AddRange([
                    "-disposition:a:0", "default",
                    "-metadata:s:a:0", "title=\"Stereo Normalized\"",
                    "-metadata:s:a:1", "title=\"Original Surround\""
                ]);
                break;

            case AudioMode.Normalized when hasNormalizedAudio:
                args.AddRange([
                    "-map", "1:a:0", "-c:a", "copy",
                    "-disposition:a:0", "default",
                    "-metadata:s:a:0", "title=\"Stereo Normalized\""
                ]);
                break;

            case AudioMode.Original:
            default:
                if (needsTranscode)
                {
                    args.AddRange(["-map", "0:a", "-c:a", "aac", "-b:a", "448k"]);
                }
                else
                {
                    args.AddRange(["-map", "0:a", "-c:a", "copy"]);
                }
                break;
        }
    }

    /// <summary>
    /// Runs FFmpeg with the given arguments
    /// </summary>
    private async Task<string> RunFFmpegAsync(
        string arguments,
        TimeSpan? totalDuration,
        CancellationToken cancellationToken,
        bool captureOutput = false,
        bool suppressProgress = false,
        bool ignoreExitCode = false)
    {
        var outputBuilder = captureOutput ? new System.Text.StringBuilder() : null;

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
            throw new Exception("Failed to start FFmpeg process");

        // Register cancellation callback to kill the process immediately
        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    LogOutput?.Invoke("Cancellation requested - killing FFmpeg process...");
                    process.Kill(true);
                }
            }
            catch
            {
                // Process may have already exited
            }
        });

        // Read stderr for progress updates
        var stderrTask = Task.Run(async () =>
        {
            try
            {
                using var reader = process.StandardError;
                while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync();
                    if (line == null) continue;

                    outputBuilder?.AppendLine(line);
                    LogOutput?.Invoke(line);

                    if (!suppressProgress && totalDuration.HasValue && totalDuration.Value.TotalSeconds > 0)
                    {
                        var match = TimeRegex().Match(line);
                        if (match.Success)
                        {
                            var current = new TimeSpan(
                                0,
                                int.Parse(match.Groups[1].Value),
                                int.Parse(match.Groups[2].Value),
                                int.Parse(match.Groups[3].Value),
                                int.Parse(match.Groups[4].Value) * 10);

                            var progress = current.TotalSeconds / totalDuration.Value.TotalSeconds * 100;
                            ProgressChanged?.Invoke(Math.Min(progress, 100));
                        }
                    }
                }
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                // Expected when process is killed
            }
        });

        // Also read stdout (usually empty for FFmpeg)
        var stdoutTask = Task.Run(async () =>
        {
            try
            {
                await process.StandardOutput.ReadToEndAsync();
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                // Expected when process is killed
            }
        });

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Ensure process is killed (registration callback should have done this)
            if (!process.HasExited)
            {
                process.Kill(true);
            }
            throw;
        }

        // Wait for stream readers to complete
        await Task.WhenAll(stderrTask, stdoutTask);

        // Check if we were cancelled
        cancellationToken.ThrowIfCancellationRequested();

        if (process.ExitCode != 0 && !ignoreExitCode)
        {
            throw new Exception($"FFmpeg exited with code {process.ExitCode}");
        }

        return outputBuilder?.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Parses loudnorm JSON output from FFmpeg
    /// </summary>
    private static LoudnormStats? ParseLoudnormOutput(string output)
    {
        try
        {
            // Find the JSON block in the output
            var jsonMatch = Regex.Match(output, @"\{[^{}]*""input_i""[^{}]*\}", RegexOptions.Singleline);
            if (!jsonMatch.Success)
                return null;

            var json = jsonMatch.Value;
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            return new LoudnormStats
            {
                InputI = root.GetProperty("input_i").GetString() ?? "",
                InputTp = root.GetProperty("input_tp").GetString() ?? "",
                InputLra = root.GetProperty("input_lra").GetString() ?? "",
                InputThresh = root.GetProperty("input_thresh").GetString() ?? "",
                TargetOffset = root.GetProperty("target_offset").GetString() ?? ""
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Cleans up temporary and partial output files
    /// </summary>
    private static void CleanupTempFiles(string outputPath, string tempAudioPath)
    {
        try
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
            if (File.Exists(tempAudioPath))
                File.Delete(tempAudioPath);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    private class LoudnormStats
    {
        public string InputI { get; init; } = "";
        public string InputTp { get; init; } = "";
        public string InputLra { get; init; } = "";
        public string InputThresh { get; init; } = "";
        public string TargetOffset { get; init; } = "";
    }
}
