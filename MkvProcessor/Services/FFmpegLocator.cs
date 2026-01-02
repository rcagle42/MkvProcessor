using System.IO;

namespace MkvProcessor.Services;

/// <summary>
/// Locates FFmpeg and FFprobe executables
/// </summary>
public static class FFmpegLocator
{
    private static string? _ffmpegPath;
    private static string? _ffprobePath;

    /// <summary>
    /// Gets the path to ffmpeg.exe
    /// </summary>
    public static string FFmpegPath => _ffmpegPath ??= FindExecutable("ffmpeg.exe");

    /// <summary>
    /// Gets the path to ffprobe.exe
    /// </summary>
    public static string FFprobePath => _ffprobePath ??= FindExecutable("ffprobe.exe");

    /// <summary>
    /// Checks if FFmpeg is available
    /// </summary>
    public static bool IsFFmpegAvailable => File.Exists(FFmpegPath);

    /// <summary>
    /// Checks if FFprobe is available
    /// </summary>
    public static bool IsFFprobeAvailable => File.Exists(FFprobePath);

    private static string FindExecutable(string name)
    {
        // First, check bundled location
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var bundledPath = Path.Combine(appDir, "ffmpeg", name);
        if (File.Exists(bundledPath))
        {
            return bundledPath;
        }

        // Second, check application directory
        var appPath = Path.Combine(appDir, name);
        if (File.Exists(appPath))
        {
            return appPath;
        }

        // Third, check PATH environment variable
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        var paths = pathEnv.Split(Path.PathSeparator);
        foreach (var path in paths)
        {
            var fullPath = Path.Combine(path, name);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        // Return the bundled path even if not found (will error later with better message)
        return bundledPath;
    }
}
