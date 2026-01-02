using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MkvProcessor.Models;
using MkvProcessor.Services;

namespace MkvProcessor.ViewModels;

/// <summary>
/// View model for the TV Show Renaming tab (three-panel layout)
/// </summary>
public partial class TvRenamerViewModel : ObservableObject
{
    private readonly SettingsService _settingsService = new();
    private readonly TvdbService _tvdbService = new();
    private readonly FileMatchingService _fileMatchingService = new();
    private readonly RenamingService _renamingService = new();

    private ProcessingSettings _settings = new();

    #region Observable Properties

    // === Search State ===

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private ObservableCollection<TvShow> _searchResults = [];

    // === Selected Show State ===

    [ObservableProperty]
    private TvShow? _selectedShow;

    [ObservableProperty]
    private bool _isLoadingShow;

    [ObservableProperty]
    private ObservableCollection<Season> _seasons = [];

    [ObservableProperty]
    private Season? _selectedSeason;

    [ObservableProperty]
    private Episode? _selectedBrowserEpisode;

    // === Recent Shows ===

    [ObservableProperty]
    private ObservableCollection<TvShow> _recentShows = [];

    // === Episode Queue (Middle Panel) ===

    [ObservableProperty]
    private ObservableCollection<Episode> _episodeQueue = [];

    [ObservableProperty]
    private Episode? _selectedQueueEpisode;

    // === File Queue (Right Panel) ===

    [ObservableProperty]
    private ObservableCollection<QueuedFile> _fileQueue = [];

    [ObservableProperty]
    private QueuedFile? _selectedQueueFile;

    // === Naming Options ===

    [ObservableProperty]
    private NamingFormat _selectedNamingFormat = NamingFormat.Standard;

    // === API Configuration ===

    [ObservableProperty]
    private string _apiKey = string.Empty;

    [ObservableProperty]
    private string _pin = string.Empty;

    [ObservableProperty]
    private bool _isApiConfigured;

    [ObservableProperty]
    private bool _isApiSettingsExpanded;

    // === Status ===

    [ObservableProperty]
    private string _statusText = "Enter your TVDB API key to get started";

    [ObservableProperty]
    private bool _isRenaming;

    #endregion

    /// <summary>
    /// Naming format options for the dropdown
    /// </summary>
    public NamingFormat[] NamingFormats => [NamingFormat.Standard, NamingFormat.Scene];

    /// <summary>
    /// Episodes for the currently selected season (for the browser)
    /// </summary>
    public ObservableCollection<Episode> CurrentSeasonEpisodes => SelectedSeason != null
        ? new ObservableCollection<Episode>(SelectedSeason.Episodes)
        : [];

    public TvRenamerViewModel()
    {
        // Wire up service events
        _tvdbService.LogOutput += message =>
        {
            Application.Current.Dispatcher.Invoke(() => StatusText = message);
        };

        _renamingService.LogOutput += message =>
        {
            Application.Current.Dispatcher.Invoke(() => StatusText = message);
        };
    }

    /// <summary>
    /// Initializes the view model (call after window loaded)
    /// </summary>
    public async Task InitializeAsync()
    {
        // Load settings
        _settings = _settingsService.Load();
        ApiKey = _settings.TvdbApiKey ?? string.Empty;
        Pin = _settings.TvdbPin ?? string.Empty;
        SelectedNamingFormat = _settings.EpisodeNamingFormat;

        IsApiConfigured = !string.IsNullOrWhiteSpace(ApiKey);
        IsApiSettingsExpanded = !IsApiConfigured;

        if (IsApiConfigured)
        {
            _tvdbService.SetApiKey(ApiKey);
            if (!string.IsNullOrWhiteSpace(Pin))
                _tvdbService.SetPin(Pin);
            StatusText = "Ready";
        }

        // Load recent shows
        await LoadRecentShowsAsync();
    }

    /// <summary>
    /// Saves current settings
    /// </summary>
    public void SaveSettings()
    {
        _settings.TvdbApiKey = ApiKey;
        _settings.TvdbPin = Pin;
        _settings.EpisodeNamingFormat = SelectedNamingFormat;
        _settingsService.Save(_settings);
    }

    #region Search Commands

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchShows()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
            return;

        IsSearching = true;
        SearchResults.Clear();

        try
        {
            var results = await _tvdbService.SearchShowsAsync(SearchQuery);
            SearchResults = new ObservableCollection<TvShow>(results);

            if (results.Count == 0)
                StatusText = "No shows found";
        }
        finally
        {
            IsSearching = false;
        }
    }

    private bool CanSearch() =>
        !string.IsNullOrWhiteSpace(SearchQuery) && IsApiConfigured && !IsSearching;

    [RelayCommand]
    private async Task SelectShow(TvShow? show)
    {
        if (show == null)
            return;

        IsLoadingShow = true;
        SearchResults.Clear();

        try
        {
            var fullShow = await _tvdbService.GetShowWithEpisodesAsync(show.Id);
            if (fullShow != null)
            {
                SelectedShow = fullShow;
                Seasons = new ObservableCollection<Season>(fullShow.Seasons.Where(s => s.Episodes.Count > 0));

                // Auto-select season 1 if available
                SelectedSeason = Seasons.FirstOrDefault(s => s.Number == 1) ?? Seasons.FirstOrDefault();

                await LoadRecentShowsAsync();
                StatusText = $"Loaded {fullShow.Name}";
            }
        }
        finally
        {
            IsLoadingShow = false;
        }
    }

    [RelayCommand]
    private async Task SelectRecentShow(TvShow? show)
    {
        if (show == null)
            return;

        await SelectShow(show);
    }

    [RelayCommand]
    private async Task RefreshShow()
    {
        if (SelectedShow == null)
            return;

        IsLoadingShow = true;

        try
        {
            var refreshedShow = await _tvdbService.RefreshShowAsync(SelectedShow.Id);
            if (refreshedShow != null)
            {
                SelectedShow = refreshedShow;
                Seasons = new ObservableCollection<Season>(refreshedShow.Seasons.Where(s => s.Episodes.Count > 0));

                // Maintain season selection
                var currentSeasonNum = SelectedSeason?.Number ?? 1;
                SelectedSeason = Seasons.FirstOrDefault(s => s.Number == currentSeasonNum) ?? Seasons.FirstOrDefault();

                StatusText = $"Refreshed {refreshedShow.Name}";
            }
        }
        finally
        {
            IsLoadingShow = false;
        }
    }

    [RelayCommand]
    private void ClearShow()
    {
        SelectedShow = null;
        Seasons.Clear();
        SelectedSeason = null;
        SearchResults.Clear();
        SearchQuery = string.Empty;
        EpisodeQueue.Clear();
        UpdateRenameCanExecute();
    }

    #endregion

    #region Episode Queue Commands

    [RelayCommand(CanExecute = nameof(CanAddEpisode))]
    private void AddEpisode()
    {
        if (SelectedBrowserEpisode == null)
            return;

        // Don't add duplicates
        if (!EpisodeQueue.Contains(SelectedBrowserEpisode))
        {
            EpisodeQueue.Add(SelectedBrowserEpisode);
            UpdateRenameCanExecute();
            StatusText = $"Added episode {SelectedBrowserEpisode.SeasonNumber}x{SelectedBrowserEpisode.EpisodeNumber:D2}";
        }
    }

    private bool CanAddEpisode() => SelectedBrowserEpisode != null;

    [RelayCommand]
    private void AddAllSeasonEpisodes()
    {
        if (SelectedSeason == null)
            return;

        var added = 0;
        foreach (var episode in SelectedSeason.Episodes.OrderBy(e => e.EpisodeNumber))
        {
            if (!EpisodeQueue.Contains(episode))
            {
                EpisodeQueue.Add(episode);
                added++;
            }
        }

        UpdateRenameCanExecute();
        StatusText = $"Added {added} episodes from Season {SelectedSeason.Number}";
    }

    [RelayCommand(CanExecute = nameof(CanRemoveEpisode))]
    private void RemoveEpisode()
    {
        if (SelectedQueueEpisode == null)
            return;

        EpisodeQueue.Remove(SelectedQueueEpisode);
        UpdateRenameCanExecute();
    }

    private bool CanRemoveEpisode() => SelectedQueueEpisode != null;

    [RelayCommand]
    private void SortEpisodes()
    {
        var sorted = EpisodeQueue.OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).ToList();
        EpisodeQueue.Clear();
        foreach (var ep in sorted)
            EpisodeQueue.Add(ep);

        StatusText = "Episodes sorted";
    }

    [RelayCommand]
    private void ClearEpisodes()
    {
        EpisodeQueue.Clear();
        UpdateRenameCanExecute();
        StatusText = "Episode queue cleared";
    }

    [RelayCommand(CanExecute = nameof(CanMoveEpisodeUp))]
    private void MoveEpisodeUp()
    {
        if (SelectedQueueEpisode == null)
            return;

        var index = EpisodeQueue.IndexOf(SelectedQueueEpisode);
        if (index > 0)
        {
            EpisodeQueue.Move(index, index - 1);
        }
    }

    private bool CanMoveEpisodeUp() =>
        SelectedQueueEpisode != null && EpisodeQueue.IndexOf(SelectedQueueEpisode) > 0;

    [RelayCommand(CanExecute = nameof(CanMoveEpisodeDown))]
    private void MoveEpisodeDown()
    {
        if (SelectedQueueEpisode == null)
            return;

        var index = EpisodeQueue.IndexOf(SelectedQueueEpisode);
        if (index < EpisodeQueue.Count - 1)
        {
            EpisodeQueue.Move(index, index + 1);
        }
    }

    private bool CanMoveEpisodeDown() =>
        SelectedQueueEpisode != null && EpisodeQueue.IndexOf(SelectedQueueEpisode) < EpisodeQueue.Count - 1;

    #endregion

    #region File Queue Commands

    [RelayCommand]
    private void AddFiles()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select video files",
            Multiselect = true,
            Filter = "Video Files|*.mkv;*.mp4;*.avi;*.m4v;*.mov;*.wmv;*.ts|All Files|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            AddFilesToQueue(dialog.FileNames);
        }
    }

    [RelayCommand]
    private void AddFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select folder containing video files"
        };

        if (dialog.ShowDialog() == true)
        {
            var files = FileMatchingService.GetVideoFilesFromDirectory(dialog.FolderName);
            AddFilesToQueue(files);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRemoveFile))]
    private void RemoveFile()
    {
        if (SelectedQueueFile == null)
            return;

        FileQueue.Remove(SelectedQueueFile);
        UpdateRenameCanExecute();
    }

    private bool CanRemoveFile() => SelectedQueueFile != null;

    [RelayCommand]
    private void SortFiles()
    {
        var sorted = FileQueue
            .OrderBy(f => f.DetectedSeasonNumber)
            .ThenBy(f => f.DetectedEpisodeNumber)
            .ThenBy(f => f.FileName)
            .ToList();

        FileQueue.Clear();
        foreach (var file in sorted)
            FileQueue.Add(file);

        StatusText = "Files sorted by detected episode number";
    }

    [RelayCommand]
    private void ClearFiles()
    {
        FileQueue.Clear();
        UpdateRenameCanExecute();
        StatusText = "File queue cleared";
    }

    [RelayCommand(CanExecute = nameof(CanMoveFileUp))]
    private void MoveFileUp()
    {
        if (SelectedQueueFile == null)
            return;

        var index = FileQueue.IndexOf(SelectedQueueFile);
        if (index > 0)
        {
            FileQueue.Move(index, index - 1);
        }
    }

    private bool CanMoveFileUp() =>
        SelectedQueueFile != null && FileQueue.IndexOf(SelectedQueueFile) > 0;

    [RelayCommand(CanExecute = nameof(CanMoveFileDown))]
    private void MoveFileDown()
    {
        if (SelectedQueueFile == null)
            return;

        var index = FileQueue.IndexOf(SelectedQueueFile);
        if (index < FileQueue.Count - 1)
        {
            FileQueue.Move(index, index + 1);
        }
    }

    private bool CanMoveFileDown() =>
        SelectedQueueFile != null && FileQueue.IndexOf(SelectedQueueFile) < FileQueue.Count - 1;

    #endregion

    #region Auto-Match and Rename Commands

    [RelayCommand(CanExecute = nameof(CanAutoMatch))]
    private void AutoMatch()
    {
        if (SelectedShow == null || FileQueue.Count == 0)
            return;

        // Clear existing episode queue
        EpisodeQueue.Clear();

        var allEpisodes = SelectedShow.Seasons.SelectMany(s => s.Episodes).ToList();
        var matchedCount = 0;
        var unmatchedFiles = new List<QueuedFile>();

        // Try to match each file by detected episode number
        foreach (var file in FileQueue)
        {
            if (file.DetectedSeasonNumber > 0 && file.DetectedEpisodeNumber > 0)
            {
                var matchingEpisode = allEpisodes.FirstOrDefault(e =>
                    e.SeasonNumber == file.DetectedSeasonNumber &&
                    e.EpisodeNumber == file.DetectedEpisodeNumber);

                if (matchingEpisode != null)
                {
                    EpisodeQueue.Add(matchingEpisode);
                    matchedCount++;
                    continue;
                }
            }

            // No match found - add placeholder (null would break things, so we track separately)
            unmatchedFiles.Add(file);
        }

        // If we have unmatched files, try extended matching (can be expanded later)
        foreach (var file in unmatchedFiles)
        {
            var matchingEpisode = TryExtendedMatch(file, allEpisodes);
            if (matchingEpisode != null)
            {
                // Find the file's position and insert episode at same position
                var fileIndex = FileQueue.IndexOf(file);
                while (EpisodeQueue.Count < fileIndex)
                {
                    // Pad with first available unmatched episode or leave gap
                }
                EpisodeQueue.Add(matchingEpisode);
                matchedCount++;
            }
        }

        UpdateRenameCanExecute();
        StatusText = $"Auto-matched {matchedCount}/{FileQueue.Count} files";
    }

    private bool CanAutoMatch() =>
        SelectedShow != null && FileQueue.Count > 0;

    /// <summary>
    /// Extended matching logic - can be expanded for fuzzy name matching, etc.
    /// </summary>
    private Episode? TryExtendedMatch(QueuedFile file, List<Episode> allEpisodes)
    {
        // TODO: Add fuzzy name matching here in the future
        // For now, just return null for unmatched files
        return null;
    }

    [RelayCommand]
    private void ClearAll()
    {
        EpisodeQueue.Clear();
        FileQueue.Clear();
        UpdateRenameCanExecute();
        StatusText = "All queues cleared";
    }

    [RelayCommand(CanExecute = nameof(CanRename))]
    private async Task Rename()
    {
        if (SelectedShow == null)
            return;

        var pairCount = Math.Min(EpisodeQueue.Count, FileQueue.Count);
        if (pairCount == 0)
        {
            StatusText = "No pairs to rename";
            return;
        }

        IsRenaming = true;

        try
        {
            var renamed = 0;
            var failed = 0;

            await Task.Run(() =>
            {
                for (int i = 0; i < pairCount; i++)
                {
                    var episode = EpisodeQueue[i];
                    var file = FileQueue[i];

                    var newFileName = _fileMatchingService.GenerateFileName(
                        episode,
                        SelectedShow.Name,
                        SelectedNamingFormat,
                        Path.GetExtension(file.FilePath));

                    var newPath = Path.Combine(Path.GetDirectoryName(file.FilePath) ?? "", newFileName);

                    // Check if target exists
                    if (File.Exists(newPath) && !string.Equals(file.FilePath, newPath, StringComparison.OrdinalIgnoreCase))
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            file.Status = "Target exists";
                        });
                        failed++;
                        continue;
                    }

                    try
                    {
                        File.Move(file.FilePath, newPath);
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            file.Status = "Renamed";
                            file.FilePath = newPath;
                            file.FileName = newFileName;
                        });
                        renamed++;
                    }
                    catch (Exception ex)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            file.Status = $"Error: {ex.Message}";
                        });
                        failed++;
                    }
                }
            });

            StatusText = $"Renamed {renamed} files" + (failed > 0 ? $", {failed} failed" : "");
        }
        finally
        {
            IsRenaming = false;
        }
    }

    private bool CanRename() =>
        SelectedShow != null &&
        EpisodeQueue.Count > 0 &&
        FileQueue.Count > 0 &&
        !IsRenaming;

    #endregion

    #region API Commands

    [RelayCommand]
    private void SaveApiKey()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            StatusText = "Please enter an API key";
            return;
        }

        _tvdbService.SetApiKey(ApiKey);
        if (!string.IsNullOrWhiteSpace(Pin))
            _tvdbService.SetPin(Pin);

        IsApiConfigured = true;
        IsApiSettingsExpanded = false;
        SaveSettings();
        StatusText = "API settings saved";
    }

    [RelayCommand]
    private async Task TestApiConnection()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            StatusText = "Please enter an API key";
            return;
        }

        _tvdbService.SetApiKey(ApiKey);
        if (!string.IsNullOrWhiteSpace(Pin))
            _tvdbService.SetPin(Pin);

        var success = await _tvdbService.AuthenticateAsync();
        if (success)
        {
            IsApiConfigured = true;
            SaveSettings();
        }
        else
        {
            IsApiConfigured = false;
        }
    }

    [RelayCommand]
    private void ClearCache()
    {
        _tvdbService.ClearCache();
        RecentShows.Clear();
        StatusText = "Cache cleared";
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Adds files from a drag-drop operation
    /// </summary>
    public void AddDroppedFiles(string[] paths)
    {
        var files = new List<string>();

        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                files.AddRange(FileMatchingService.GetVideoFilesFromDirectory(path));
            }
            else if (File.Exists(path) && FileMatchingService.IsSupportedVideoFile(path))
            {
                files.Add(path);
            }
        }

        if (files.Count > 0)
        {
            AddFilesToQueue(files);
        }
    }

    #endregion

    #region Property Change Handlers

    partial void OnSelectedSeasonChanged(Season? value)
    {
        OnPropertyChanged(nameof(CurrentSeasonEpisodes));
    }

    partial void OnSelectedQueueEpisodeChanged(Episode? value)
    {
        MoveEpisodeUpCommand.NotifyCanExecuteChanged();
        MoveEpisodeDownCommand.NotifyCanExecuteChanged();
        RemoveEpisodeCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedQueueFileChanged(QueuedFile? value)
    {
        MoveFileUpCommand.NotifyCanExecuteChanged();
        MoveFileDownCommand.NotifyCanExecuteChanged();
        RemoveFileCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedBrowserEpisodeChanged(Episode? value)
    {
        AddEpisodeCommand.NotifyCanExecuteChanged();
    }

    partial void OnSearchQueryChanged(string value)
    {
        SearchShowsCommand.NotifyCanExecuteChanged();
    }

    partial void OnApiKeyChanged(string value)
    {
        SearchShowsCommand.NotifyCanExecuteChanged();
    }

    #endregion

    #region Private Methods

    private async Task LoadRecentShowsAsync()
    {
        await Task.Run(() =>
        {
            var recent = _tvdbService.GetRecentShows();
            Application.Current.Dispatcher.Invoke(() =>
            {
                RecentShows = new ObservableCollection<TvShow>(recent);
            });
        });
    }

    private void AddFilesToQueue(IEnumerable<string> filePaths)
    {
        var added = 0;
        foreach (var filePath in filePaths)
        {
            // Don't add duplicates
            if (FileQueue.Any(f => f.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
                continue;

            var fileName = Path.GetFileName(filePath);
            var (season, episode, _) = _fileMatchingService.DetectEpisode(fileName);

            FileQueue.Add(new QueuedFile
            {
                FilePath = filePath,
                FileName = fileName,
                DetectedSeasonNumber = season,
                DetectedEpisodeNumber = episode
            });
            added++;
        }

        UpdateRenameCanExecute();
        StatusText = $"Added {added} files";
    }

    private void UpdateRenameCanExecute()
    {
        RenameCommand.NotifyCanExecuteChanged();
        AutoMatchCommand.NotifyCanExecuteChanged();
    }

    #endregion
}

/// <summary>
/// Represents a file in the file queue
/// </summary>
public partial class QueuedFile : ObservableObject
{
    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private int _detectedSeasonNumber;

    [ObservableProperty]
    private int _detectedEpisodeNumber;

    [ObservableProperty]
    private string _status = string.Empty;

    /// <summary>
    /// Display string showing detected episode info
    /// </summary>
    public string DetectedInfo => DetectedSeasonNumber > 0 && DetectedEpisodeNumber > 0
        ? $"({DetectedSeasonNumber}x{DetectedEpisodeNumber:D2})"
        : "";
}
