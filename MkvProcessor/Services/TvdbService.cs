using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using MkvProcessor.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MkvProcessor.Services;

/// <summary>
/// Service for interacting with the TVDB v4 API
/// </summary>
public class TvdbService
{
    private const string BaseUrl = "https://api4.thetvdb.com/v4/";

    private readonly TvdbCacheService _cacheService;
    private readonly HttpClient _httpClient;

    private string? _apiKey;
    private string? _pin;
    private string? _bearerToken;
    private DateTime _tokenExpiration;

    /// <summary>Event raised when log messages are generated</summary>
    public event Action<string>? LogOutput;

    /// <summary>Whether the service has a valid API key set</summary>
    public bool HasApiKey => !string.IsNullOrWhiteSpace(_apiKey);

    /// <summary>Whether the service is currently authenticated</summary>
    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(_bearerToken) && DateTime.UtcNow < _tokenExpiration;

    public TvdbService()
    {
        _cacheService = new TvdbCacheService();
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(BaseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    /// <summary>
    /// Sets the API key for authentication
    /// </summary>
    public void SetApiKey(string apiKey)
    {
        _apiKey = apiKey;
        _bearerToken = null; // Clear token when key changes
    }

    /// <summary>
    /// Sets the subscriber PIN (optional, only needed for user-supported keys)
    /// </summary>
    public void SetPin(string? pin)
    {
        _pin = pin;
        _bearerToken = null; // Clear token when pin changes
    }

    /// <summary>
    /// Authenticates with TVDB and obtains bearer token
    /// </summary>
    public async Task<bool> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            LogOutput?.Invoke("Error: No API key configured");
            return false;
        }

        try
        {
            // Build login payload - include PIN only if provided
            object loginData = string.IsNullOrWhiteSpace(_pin)
                ? new { apikey = _apiKey }
                : new { apikey = _apiKey, pin = _pin };

            var content = new StringContent(
                JsonConvert.SerializeObject(loginData),
                Encoding.UTF8,
                "application/json");

            LogOutput?.Invoke("Authenticating with TVDB...");
            var response = await _httpClient.PostAsync("login", content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                LogOutput?.Invoke($"Authentication failed: {response.StatusCode} - {errorBody}");
                return false;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JObject.Parse(json);

            _bearerToken = result["data"]?["token"]?.ToString();

            if (string.IsNullOrEmpty(_bearerToken))
            {
                LogOutput?.Invoke("Authentication failed: No token received");
                return false;
            }

            // TVDB tokens expire in 30 days, but we'll refresh after 7 days to be safe
            _tokenExpiration = DateTime.UtcNow.AddDays(7);

            LogOutput?.Invoke("Successfully authenticated with TVDB");
            return true;
        }
        catch (HttpRequestException ex)
        {
            LogOutput?.Invoke($"Network error: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            LogOutput?.Invoke($"Authentication error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Searches for shows by name
    /// </summary>
    public async Task<List<TvShow>> SearchShowsAsync(string query, CancellationToken cancellationToken = default)
    {
        if (!await EnsureAuthenticatedAsync(cancellationToken))
            return [];

        try
        {
            var encodedQuery = Uri.EscapeDataString(query);
            var request = CreateAuthenticatedRequest(HttpMethod.Get, $"search?query={encodedQuery}&type=series");
            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                LogOutput?.Invoke($"Search failed: {response.StatusCode}");
                return [];
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JObject.Parse(json);
            var data = result["data"] as JArray;

            if (data == null)
                return [];

            var shows = new List<TvShow>();
            foreach (var item in data)
            {
                var show = ParseSearchResult(item);
                if (show != null)
                    shows.Add(show);
            }

            LogOutput?.Invoke($"Found {shows.Count} shows for '{query}'");
            return shows;
        }
        catch (Exception ex)
        {
            LogOutput?.Invoke($"Search error: {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// Gets full show details with seasons and episodes
    /// </summary>
    public async Task<TvShow?> GetShowWithEpisodesAsync(int showId, bool useCache = true, CancellationToken cancellationToken = default)
    {
        // Check cache first
        if (useCache)
        {
            var cached = _cacheService.LoadShow(showId);
            if (cached != null)
            {
                LogOutput?.Invoke($"Loaded '{cached.Name}' from cache");
                _cacheService.AddToRecent(cached);
                return cached;
            }
        }

        if (!await EnsureAuthenticatedAsync(cancellationToken))
            return null;

        try
        {
            // Get show details
            var showRequest = CreateAuthenticatedRequest(HttpMethod.Get, $"series/{showId}/extended");
            var showResponse = await _httpClient.SendAsync(showRequest, cancellationToken);

            if (!showResponse.IsSuccessStatusCode)
            {
                LogOutput?.Invoke($"Failed to get show details: {showResponse.StatusCode}");
                return null;
            }

            var showJson = await showResponse.Content.ReadAsStringAsync(cancellationToken);
            var showResult = JObject.Parse(showJson);
            var showData = showResult["data"];

            if (showData == null)
                return null;

            var show = ParseShowDetails(showData);
            if (show == null)
                return null;

            // Get episodes
            var episodes = await GetAllEpisodesAsync(showId, cancellationToken);
            OrganizeEpisodesBySeason(show, episodes);

            // Cache the result
            _cacheService.SaveShow(show);
            _cacheService.AddToRecent(show);

            LogOutput?.Invoke($"Loaded '{show.Name}' with {show.Seasons.Count} seasons");
            return show;
        }
        catch (Exception ex)
        {
            LogOutput?.Invoke($"Error getting show: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Refreshes cached show data from API
    /// </summary>
    public async Task<TvShow?> RefreshShowAsync(int showId, CancellationToken cancellationToken = default)
    {
        _cacheService.RemoveFromCache(showId);
        return await GetShowWithEpisodesAsync(showId, useCache: false, cancellationToken);
    }

    /// <summary>
    /// Gets recently accessed shows from cache
    /// </summary>
    public List<TvShow> GetRecentShows()
    {
        return _cacheService.GetRecentShows();
    }

    /// <summary>
    /// Clears all cached data
    /// </summary>
    public void ClearCache()
    {
        _cacheService.ClearCache();
        LogOutput?.Invoke("Cache cleared");
    }

    private async Task<bool> EnsureAuthenticatedAsync(CancellationToken cancellationToken)
    {
        if (IsAuthenticated)
            return true;

        return await AuthenticateAsync(cancellationToken);
    }

    private HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, string endpoint)
    {
        var request = new HttpRequestMessage(method, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _bearerToken);
        return request;
    }

    private async Task<List<Episode>> GetAllEpisodesAsync(int showId, CancellationToken cancellationToken)
    {
        var episodes = new List<Episode>();
        int page = 0;

        while (true)
        {
            var request = CreateAuthenticatedRequest(HttpMethod.Get, $"series/{showId}/episodes/default?page={page}");
            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
                break;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JObject.Parse(json);
            var data = result["data"]?["episodes"] as JArray;

            if (data == null || data.Count == 0)
                break;

            foreach (var item in data)
            {
                var episode = ParseEpisode(item);
                if (episode != null)
                    episodes.Add(episode);
            }

            // Check if there are more pages
            var links = result["links"];
            var next = links?["next"]?.ToString();
            if (string.IsNullOrEmpty(next))
                break;

            page++;
        }

        return episodes;
    }

    private void OrganizeEpisodesBySeason(TvShow show, List<Episode> episodes)
    {
        var seasonGroups = episodes
            .GroupBy(e => e.SeasonNumber)
            .OrderBy(g => g.Key);

        show.Seasons = seasonGroups.Select(g => new Season
        {
            Number = g.Key,
            Episodes = g.OrderBy(e => e.EpisodeNumber).ToList()
        }).ToList();
    }

    private TvShow? ParseSearchResult(JToken item)
    {
        try
        {
            var id = item["tvdb_id"]?.ToString();
            if (string.IsNullOrEmpty(id) || !int.TryParse(id, out var showId))
                return null;

            var year = item["year"]?.ToString();
            int? yearInt = null;
            if (!string.IsNullOrEmpty(year) && int.TryParse(year, out var y))
                yearInt = y;

            return new TvShow
            {
                Id = showId,
                Name = item["name"]?.ToString() ?? "Unknown",
                Year = yearInt,
                Status = item["status"]?.ToString() ?? "",
                Network = item["network"]?.ToString(),
                Overview = item["overview"]?.ToString(),
                PosterUrl = item["image_url"]?.ToString()
            };
        }
        catch
        {
            return null;
        }
    }

    private TvShow? ParseShowDetails(JToken item)
    {
        try
        {
            var id = item["id"]?.Value<int>() ?? 0;
            if (id == 0)
                return null;

            var year = item["year"]?.Value<int?>();

            return new TvShow
            {
                Id = id,
                Name = item["name"]?.ToString() ?? "Unknown",
                Year = year,
                Status = item["status"]?["name"]?.ToString() ?? "",
                Network = item["originalNetwork"]?["name"]?.ToString(),
                Overview = item["overview"]?.ToString(),
                PosterUrl = item["image"]?.ToString()
            };
        }
        catch
        {
            return null;
        }
    }

    private Episode? ParseEpisode(JToken item)
    {
        try
        {
            return new Episode
            {
                Id = item["id"]?.Value<int>() ?? 0,
                Name = item["name"]?.ToString() ?? "Unknown",
                SeasonNumber = item["seasonNumber"]?.Value<int>() ?? 0,
                EpisodeNumber = item["number"]?.Value<int>() ?? 0,
                AiredDate = item["aired"]?.ToString(),
                Overview = item["overview"]?.ToString(),
                Runtime = item["runtime"]?.Value<int?>()
            };
        }
        catch
        {
            return null;
        }
    }
}
