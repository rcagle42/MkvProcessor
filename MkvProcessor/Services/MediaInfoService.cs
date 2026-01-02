using System.Diagnostics;
using System.IO;
using System.Text.Json;
using MkvProcessor.Models;

namespace MkvProcessor.Services;

/// <summary>
/// Service for probing media file information using FFprobe
/// </summary>
public class MediaInfoService
{
    /// <summary>
    /// Gets information about an MKV file
    /// </summary>
    public async Task<MkvFile> GetFileInfoAsync(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        var mkvFile = new MkvFile
        {
            FilePath = filePath,
            FileName = fileInfo.Name,
            FileSize = fileInfo.Length
        };

        try
        {
            // Get duration and stream info using ffprobe JSON output
            var probeResult = await RunFFprobeAsync(filePath);

            if (probeResult.HasValue)
            {
                var result = probeResult.Value;

                // Parse duration
                if (result.TryGetProperty("format", out var format) &&
                    format.TryGetProperty("duration", out var durationProp))
                {
                    if (double.TryParse(durationProp.GetString(), out var durationSeconds))
                    {
                        mkvFile.Duration = TimeSpan.FromSeconds(durationSeconds);
                    }
                }

                // Parse streams
                if (result.TryGetProperty("streams", out var streams))
                {
                    foreach (var stream in streams.EnumerateArray())
                    {
                        if (stream.TryGetProperty("codec_type", out var codecType))
                        {
                            var type = codecType.GetString();

                            if (type == "audio" && string.IsNullOrEmpty(mkvFile.AudioCodec))
                            {
                                if (stream.TryGetProperty("codec_name", out var codecName))
                                {
                                    mkvFile.AudioCodec = codecName.GetString() ?? "unknown";
                                }
                            }
                            else if (type == "subtitle")
                            {
                                mkvFile.HasSubtitles = true;
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // If probing fails, we still return the file with basic info
        }

        return mkvFile;
    }

    /// <summary>
    /// Gets information about multiple MKV files
    /// </summary>
    public async Task<List<MkvFile>> GetFilesInfoAsync(IEnumerable<string> filePaths, IProgress<int>? progress = null)
    {
        var files = filePaths.ToList();
        var results = new List<MkvFile>();
        var completed = 0;

        foreach (var filePath in files)
        {
            var mkvFile = await GetFileInfoAsync(filePath);
            results.Add(mkvFile);
            completed++;
            progress?.Report((int)((double)completed / files.Count * 100));
        }

        return results;
    }

    /// <summary>
    /// Gets all MKV files in a directory
    /// </summary>
    public IEnumerable<string> GetMkvFilesInDirectory(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
            return [];

        return Directory.GetFiles(directoryPath, "*.mkv", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f);
    }

    /// <summary>
    /// Runs FFprobe and returns parsed JSON result
    /// </summary>
    private async Task<JsonElement?> RunFFprobeAsync(string filePath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = FFmpegLocator.FFprobePath,
            Arguments = $"-hide_banner -loglevel error -print_format json -show_format -show_streams \"{filePath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process == null)
            return null;

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(output);
            return doc.RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets detailed subtitle stream info
    /// </summary>
    public async Task<List<SubtitleStreamInfo>> GetSubtitleStreamsAsync(string filePath)
    {
        var streams = new List<SubtitleStreamInfo>();

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = FFmpegLocator.FFprobePath,
                Arguments = $"-hide_banner -loglevel error -select_streams s -show_entries stream=index,codec_name -of csv=p=0 \"{filePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return streams;

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var parts = line.Trim().Split(',');
                    if (parts.Length >= 2 && int.TryParse(parts[0], out var index))
                    {
                        streams.Add(new SubtitleStreamInfo
                        {
                            Index = index,
                            CodecName = parts[1],
                            IsPgs = parts[1].Contains("pgs", StringComparison.OrdinalIgnoreCase) ||
                                   parts[1].Contains("hdmv", StringComparison.OrdinalIgnoreCase)
                        });
                    }
                }
            }
        }
        catch
        {
            // Return empty list on error
        }

        return streams;
    }
}

/// <summary>
/// Information about a subtitle stream
/// </summary>
public class SubtitleStreamInfo
{
    public int Index { get; init; }
    public string CodecName { get; init; } = string.Empty;
    public bool IsPgs { get; init; }
}
