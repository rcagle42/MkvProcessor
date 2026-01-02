using System.IO;
using MkvProcessor.Models;
using Newtonsoft.Json;

namespace MkvProcessor.Services;

/// <summary>
/// Service for loading and saving application settings
/// </summary>
public class SettingsService
{
    private readonly string _settingsPath;

    public SettingsService()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appFolder = Path.Combine(appDataPath, "MkvProcessor");
        Directory.CreateDirectory(appFolder);
        _settingsPath = Path.Combine(appFolder, "settings.json");
    }

    /// <summary>
    /// Loads settings from disk, or returns defaults if not found
    /// </summary>
    public ProcessingSettings Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                var settings = JsonConvert.DeserializeObject<ProcessingSettings>(json);
                return settings ?? new ProcessingSettings();
            }
        }
        catch (Exception)
        {
            // If loading fails, return defaults
        }

        return new ProcessingSettings();
    }

    /// <summary>
    /// Saves settings to disk
    /// </summary>
    public void Save(ProcessingSettings settings)
    {
        try
        {
            var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
            File.WriteAllText(_settingsPath, json);
        }
        catch (Exception)
        {
            // Silently fail if we can't save settings
        }
    }
}
