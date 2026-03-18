using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using MkvProcessor.Models;

namespace MkvProcessor.Services;

/// <summary>
/// Result of a subtitle conversion operation
/// </summary>
public class SubtitleConversionResult
{
    public bool Success { get; set; }
    public string? OutputPath { get; set; }
    public string? ErrorMessage { get; set; }
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// Service for converting PGS/SUP subtitles to SRT using PgsToSrt
/// </summary>
public partial class PgsToSrtService
{
    /// <summary>Event raised when log messages are generated</summary>
    public event Action<string>? LogOutput;

    /// <summary>Event raised when conversion progress changes (0-100)</summary>
    public event Action<double>? ProgressChanged;

    /// <summary>
    /// Checks if PgsToSrt is available
    /// </summary>
    public bool IsAvailable => PgsToSrtLocator.IsAvailable;

    /// <summary>
    /// Gets the path to PgsToSrt.dll
    /// </summary>
    public string PgsToSrtPath => PgsToSrtLocator.PgsToSrtPath;

    /// <summary>
    /// Validates that PgsToSrt is properly installed
    /// </summary>
    public (bool IsValid, string Message) ValidateInstallation()
    {
        if (!PgsToSrtLocator.IsAvailable)
        {
            return (false, $"PgsToSrt.dll not found. Expected at: {PgsToSrtLocator.PgsToSrtPath}");
        }

        return (true, "PgsToSrt is available");
    }

    /// <summary>
    /// Gets available language codes from tessdata directory
    /// </summary>
    public List<string> GetAvailableLanguages(string? tessdataPath)
    {
        var languages = new List<string>();

        if (string.IsNullOrEmpty(tessdataPath) || !Directory.Exists(tessdataPath))
            return languages;

        try
        {
            var trainedDataFiles = Directory.GetFiles(tessdataPath, "*.traineddata");
            foreach (var file in trainedDataFiles)
            {
                var langCode = Path.GetFileNameWithoutExtension(file);
                if (!string.IsNullOrEmpty(langCode))
                    languages.Add(langCode);
            }
        }
        catch
        {
            // Ignore errors reading directory
        }

        return languages.OrderBy(l => l).ToList();
    }

    /// <summary>
    /// Converts a SUP file to SRT
    /// </summary>
    public async Task<SubtitleConversionResult> ConvertAsync(
        SubtitleFile file,
        string language,
        string? tessdataPath,
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.Now;
        var result = new SubtitleConversionResult();

        if (!PgsToSrtLocator.IsAvailable)
        {
            result.ErrorMessage = "PgsToSrt.dll not found";
            return result;
        }

        if (!File.Exists(file.FilePath))
        {
            result.ErrorMessage = "Input file not found";
            return result;
        }

        var outputPath = file.ExpectedOutputPath;

        try
        {
            // Build arguments
            var args = new StringBuilder();
            args.Append($"--input \"{file.FilePath}\"");
            args.Append($" --output \"{outputPath}\"");
            args.Append($" --tesseractlanguage {language}");

            if (!string.IsNullOrEmpty(tessdataPath) && Directory.Exists(tessdataPath))
            {
                args.Append($" --tesseractdata \"{tessdataPath}\"");
            }

            LogOutput?.Invoke($"Converting: {file.FileName}");
            LogOutput?.Invoke($"Language: {language}");

            var success = await RunPgsToSrtAsync(args.ToString(), cancellationToken);

            if (success && File.Exists(outputPath))
            {
                // Apply OCR corrections
                LogOutput?.Invoke("Applying OCR corrections...");
                await SrtPostProcessor.ProcessFileAsync(outputPath);

                result.Success = true;
                result.OutputPath = outputPath;
                LogOutput?.Invoke($"Complete: {Path.GetFileName(outputPath)}");
            }
            else if (!cancellationToken.IsCancellationRequested)
            {
                result.ErrorMessage = "Conversion failed - no output file created";
            }
        }
        catch (OperationCanceledException)
        {
            result.ErrorMessage = "Cancelled";
            throw;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            LogOutput?.Invoke($"Error: {ex.Message}");
        }

        result.Duration = DateTime.Now - startTime;
        return result;
    }

    private async Task<bool> RunPgsToSrtAsync(string arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{PgsToSrtLocator.PgsToSrtPath}\" {arguments}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            LogOutput?.Invoke("Failed to start PgsToSrt process");
            return false;
        }

        // Reduce priority so OCR doesn't starve the system
        try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }

        // Register cancellation callback
        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    LogOutput?.Invoke("Cancellation requested - stopping conversion...");
                    process.Kill(true);
                }
            }
            catch { }
        });

        // Read output for progress updates
        var outputTask = Task.Run(async () =>
        {
            try
            {
                using var reader = process.StandardOutput;
                while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(cancellationToken);
                    if (string.IsNullOrEmpty(line)) continue;

                    LogOutput?.Invoke(line);
                    ParseProgressFromOutput(line);
                }
            }
            catch (OperationCanceledException) { }
            catch { }
        }, cancellationToken);

        var errorTask = Task.Run(async () =>
        {
            try
            {
                using var reader = process.StandardError;
                while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(cancellationToken);
                    if (!string.IsNullOrEmpty(line))
                    {
                        LogOutput?.Invoke($"[stderr] {line}");
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch { }
        }, cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(true);
            throw;
        }

        await Task.WhenAll(outputTask, errorTask);
        cancellationToken.ThrowIfCancellationRequested();

        return process.ExitCode == 0;
    }

    private void ParseProgressFromOutput(string line)
    {
        // Try to parse progress from PgsToSrt output
        // Example patterns: "Processing subtitle 50/100" or percentage-based
        var match = ProgressRegex().Match(line);
        if (match.Success)
        {
            if (int.TryParse(match.Groups[1].Value, out var current) &&
                int.TryParse(match.Groups[2].Value, out var total) &&
                total > 0)
            {
                var progress = (current * 100.0) / total;
                ProgressChanged?.Invoke(progress);
            }
        }

        // Also check for percentage patterns
        var percentMatch = PercentRegex().Match(line);
        if (percentMatch.Success && double.TryParse(percentMatch.Groups[1].Value, out var percent))
        {
            ProgressChanged?.Invoke(percent);
        }
    }

    [GeneratedRegex(@"(\d+)\s*/\s*(\d+)")]
    private static partial Regex ProgressRegex();

    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*%")]
    private static partial Regex PercentRegex();
}
