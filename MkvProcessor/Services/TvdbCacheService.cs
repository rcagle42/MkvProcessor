using System.IO;
using MkvProcessor.Models;
using Newtonsoft.Json;

namespace MkvProcessor.Services;

/// <summary>
/// Service for caching TVDB show data locally as JSON files
/// </summary>
public class TvdbCacheService
{
    private readonly string _cacheFolder;
    private readonly string _showsFolder;
    private readonly string _recentPath;

    /// <summary>Cache expiration time (default: 7 days)</summary>
    public TimeSpan CacheExpiration { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Maximum number of recent shows to keep</summary>
    public int MaxRecentShows { get; set; } = 10;

    public TvdbCacheService()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appFolder = Path.Combine(appDataPath, "MkvProcessor");
        _cacheFolder = Path.Combine(appFolder, "TvdbCache");
        _showsFolder = Path.Combine(_cacheFolder, "shows");
        _recentPath = Path.Combine(_cacheFolder, "recent.json");

        Directory.CreateDirectory(_cacheFolder);
        Directory.CreateDirectory(_showsFolder);
    }

    /// <summary>
    /// Saves show data to cache
    /// </summary>
    public void SaveShow(TvShow show)
    {
        try
        {
            show.CachedAt = DateTime.UtcNow;
            var filePath = GetShowFilePath(show.Id);
            var json = JsonConvert.SerializeObject(show, Formatting.Indented);
            File.WriteAllText(filePath, json);
        }
        catch (Exception)
        {
            // Silently fail if we can't save cache
        }
    }

    /// <summary>
    /// Loads show from cache, returns null if not found or expired
    /// </summary>
    public TvShow? LoadShow(int showId)
    {
        try
        {
            var filePath = GetShowFilePath(showId);
            if (!File.Exists(filePath))
                return null;

            var json = File.ReadAllText(filePath);
            var show = JsonConvert.DeserializeObject<TvShow>(json);

            if (show == null)
                return null;

            // Check if cache has expired
            if (DateTime.UtcNow - show.CachedAt > CacheExpiration)
                return null;

            return show;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Checks if show is cached and not expired
    /// </summary>
    public bool IsCached(int showId)
    {
        return LoadShow(showId) != null;
    }

    /// <summary>
    /// Gets list of recently accessed shows
    /// </summary>
    public List<TvShow> GetRecentShows()
    {
        try
        {
            if (!File.Exists(_recentPath))
                return [];

            var json = File.ReadAllText(_recentPath);
            var recent = JsonConvert.DeserializeObject<List<RecentShowEntry>>(json);

            if (recent == null)
                return [];

            // Load full show data for each recent entry
            var shows = new List<TvShow>();
            foreach (var entry in recent)
            {
                var show = LoadShow(entry.Id);
                if (show != null)
                {
                    shows.Add(show);
                }
            }

            return shows;
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>
    /// Adds show to recent list
    /// </summary>
    public void AddToRecent(TvShow show)
    {
        try
        {
            var recent = GetRecentEntries();

            // Remove if already exists
            recent.RemoveAll(r => r.Id == show.Id);

            // Add to front
            recent.Insert(0, new RecentShowEntry
            {
                Id = show.Id,
                Name = show.Name,
                Year = show.Year
            });

            // Trim to max
            if (recent.Count > MaxRecentShows)
                recent = recent.Take(MaxRecentShows).ToList();

            var json = JsonConvert.SerializeObject(recent, Formatting.Indented);
            File.WriteAllText(_recentPath, json);
        }
        catch (Exception)
        {
            // Silently fail
        }
    }

    /// <summary>
    /// Removes specific show from cache
    /// </summary>
    public void RemoveFromCache(int showId)
    {
        try
        {
            var filePath = GetShowFilePath(showId);
            if (File.Exists(filePath))
                File.Delete(filePath);

            // Also remove from recent
            var recent = GetRecentEntries();
            recent.RemoveAll(r => r.Id == showId);
            var json = JsonConvert.SerializeObject(recent, Formatting.Indented);
            File.WriteAllText(_recentPath, json);
        }
        catch (Exception)
        {
            // Silently fail
        }
    }

    /// <summary>
    /// Clears all cached data
    /// </summary>
    public void ClearCache()
    {
        try
        {
            if (Directory.Exists(_showsFolder))
            {
                foreach (var file in Directory.GetFiles(_showsFolder, "*.json"))
                {
                    File.Delete(file);
                }
            }

            if (File.Exists(_recentPath))
                File.Delete(_recentPath);
        }
        catch (Exception)
        {
            // Silently fail
        }
    }

    private string GetShowFilePath(int showId)
    {
        return Path.Combine(_showsFolder, $"{showId}.json");
    }

    private List<RecentShowEntry> GetRecentEntries()
    {
        try
        {
            if (!File.Exists(_recentPath))
                return [];

            var json = File.ReadAllText(_recentPath);
            return JsonConvert.DeserializeObject<List<RecentShowEntry>>(json) ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>
    /// Lightweight entry for the recent shows list
    /// </summary>
    private class RecentShowEntry
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int? Year { get; set; }
    }
}
