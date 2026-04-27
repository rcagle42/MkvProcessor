using System.IO;

namespace MkvProcessor.Services.Subtitle;

/// <summary>
/// Locates mkvextract.exe (from MKVToolNix) using the same fallback chain as
/// <see cref="PgsToSrtLocator"/>: user-configured path → bundled /mkvtoolnix/ folder →
/// application directory → system PATH. Returns the bundled path as a sentinel when not
/// found so error messages can include a sensible "expected location" hint.
/// </summary>
public static class MkvExtractLocator
{
    private static string? _mkvExtractPath;
    private static string? _userConfiguredPath;

    public static string MkvExtractPath => _mkvExtractPath ??= FindMkvExtract();

    public static bool IsAvailable => File.Exists(MkvExtractPath);

    public static void SetUserPath(string? path)
    {
        _userConfiguredPath = path;
        _mkvExtractPath = null;
    }

    public static void Refresh() => _mkvExtractPath = null;

    private static string FindMkvExtract()
    {
        const string exeName = "mkvextract.exe";

        // 1. User-configured path (direct file or directory containing the exe)
        if (!string.IsNullOrWhiteSpace(_userConfiguredPath))
        {
            if (File.Exists(_userConfiguredPath))
                return _userConfiguredPath;

            var inDir = Path.Combine(_userConfiguredPath, exeName);
            if (File.Exists(inDir))
                return inDir;
        }

        var appDir = AppDomain.CurrentDomain.BaseDirectory;

        // 2. Bundled /mkvtoolnix/ folder
        var bundledPath = Path.Combine(appDir, "mkvtoolnix", exeName);
        if (File.Exists(bundledPath))
            return bundledPath;

        // 3. Application directory
        var appPath = Path.Combine(appDir, exeName);
        if (File.Exists(appPath))
            return appPath;

        // 4. System PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            try
            {
                var full = Path.Combine(dir, exeName);
                if (File.Exists(full))
                    return full;
            }
            catch
            {
                // ignore invalid path entries
            }
        }

        // Return bundled path as sentinel even when not found
        return bundledPath;
    }
}
