using System.IO;

namespace MkvProcessor.Services;

/// <summary>
/// Locates PgsToSrt.dll executable for subtitle conversion
/// </summary>
public static class PgsToSrtLocator
{
    private static string? _pgsToSrtPath;
    private static string? _userConfiguredPath;

    /// <summary>
    /// Gets the path to PgsToSrt.dll
    /// </summary>
    public static string PgsToSrtPath => _pgsToSrtPath ??= FindPgsToSrt();

    /// <summary>
    /// Whether PgsToSrt was found
    /// </summary>
    public static bool IsAvailable => File.Exists(PgsToSrtPath);

    /// <summary>
    /// Sets a user-configured path (takes priority over auto-detection)
    /// </summary>
    public static void SetUserPath(string? path)
    {
        _userConfiguredPath = path;
        _pgsToSrtPath = null; // Reset to re-evaluate
    }

    /// <summary>
    /// Refreshes the cached path
    /// </summary>
    public static void Refresh()
    {
        _pgsToSrtPath = null;
    }

    private static string FindPgsToSrt()
    {
        const string dllName = "PgsToSrt.dll";

        // 1. Check user-configured path first
        if (!string.IsNullOrWhiteSpace(_userConfiguredPath))
        {
            // Could be a direct path to the DLL or a directory containing it
            if (File.Exists(_userConfiguredPath))
                return _userConfiguredPath;

            var inDir = Path.Combine(_userConfiguredPath, dllName);
            if (File.Exists(inDir))
                return inDir;
        }

        var appDir = AppDomain.CurrentDomain.BaseDirectory;

        // 2. Check bundled location: /pgstosrt/
        var bundledPath = Path.Combine(appDir, "pgstosrt", dllName);
        if (File.Exists(bundledPath))
            return bundledPath;

        // 3. Check application directory
        var appPath = Path.Combine(appDir, dllName);
        if (File.Exists(appPath))
            return appPath;

        // 4. Check PATH environment variable
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        var paths = pathEnv.Split(Path.PathSeparator);
        foreach (var path in paths)
        {
            try
            {
                var fullPath = Path.Combine(path, dllName);
                if (File.Exists(fullPath))
                    return fullPath;
            }
            catch
            {
                // Skip invalid paths
            }
        }

        // Return bundled path even if not found (will error later with clear message)
        return bundledPath;
    }

    /// <summary>
    /// Gets the directory containing PgsToSrt.dll (for relative tessdata lookup)
    /// </summary>
    public static string? GetPgsToSrtDirectory()
    {
        if (IsAvailable)
            return Path.GetDirectoryName(PgsToSrtPath);
        return null;
    }

    /// <summary>
    /// Attempts to find tessdata directory relative to PgsToSrt location
    /// </summary>
    public static string? FindTessdata()
    {
        var pgsDir = GetPgsToSrtDirectory();
        if (pgsDir == null)
            return null;

        // Check for tessdata in same directory as PgsToSrt
        var tessdata = Path.Combine(pgsDir, "tessdata");
        if (Directory.Exists(tessdata))
            return tessdata;

        // Check parent directory
        var parentDir = Path.GetDirectoryName(pgsDir);
        if (parentDir != null)
        {
            tessdata = Path.Combine(parentDir, "tessdata");
            if (Directory.Exists(tessdata))
                return tessdata;
        }

        return null;
    }
}
