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

    // === Rename Preview Grid ===

    [ObservableProperty]
    private ObservableCollection<RenamePreviewItem> _renamePreviewItems = [];

    [ObservableProperty]
    private RenamePreviewItem? _selectedPreviewItem;

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
        // Clear episode matches from preview items
        foreach (var item in RenamePreviewItems)
        {
            item.MatchedEpisode = null;
            item.NewFileName = string.Empty;
            item.Status = MatchStatus.Unmatched;
        }
        UpdateRenameCanExecute();
    }

    #endregion

    #region Episode Assignment Commands

    /// <summary>
    /// Assigns the selected browser episode to the selected preview item
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAssignEpisode))]
    private void AssignEpisode()
    {
        if (SelectedBrowserEpisode == null || SelectedPreviewItem == null || SelectedShow == null)
            return;

        SelectedPreviewItem.MatchedEpisode = SelectedBrowserEpisode;
        SelectedPreviewItem.NewFileName = _fileMatchingService.GenerateFileName(
            SelectedBrowserEpisode,
            SelectedShow.Name,
            SelectedNamingFormat,
            SelectedPreviewItem.Extension);
        SelectedPreviewItem.Status = MatchStatus.Matched;

        UpdateRenameCanExecute();
        StatusText = $"Assigned {SelectedBrowserEpisode.SeasonNumber}x{SelectedBrowserEpisode.EpisodeNumber:D2} to selected file";
    }

    private bool CanAssignEpisode() =>
        SelectedBrowserEpisode != null && SelectedPreviewItem != null && SelectedShow != null;

    /// <summary>
    /// Clears the episode assignment from the selected preview item
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanClearAssignment))]
    private void ClearAssignment()
    {
        if (SelectedPreviewItem == null)
            return;

        SelectedPreviewItem.MatchedEpisode = null;
        SelectedPreviewItem.NewFileName = string.Empty;
        SelectedPreviewItem.Status = MatchStatus.Unmatched;

        UpdateRenameCanExecute();
        StatusText = "Assignment cleared";
    }

    private bool CanClearAssignment() =>
        SelectedPreviewItem != null && SelectedPreviewItem.HasMatch;

    #endregion

    #region Preview Grid Commands

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
            AddFilesToPreview(dialog.FileNames);
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
            AddFilesToPreview(files);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRemoveItem))]
    private void RemoveItem()
    {
        if (SelectedPreviewItem == null)
            return;

        RenamePreviewItems.Remove(SelectedPreviewItem);
        UpdateRenameCanExecute();
    }

    private bool CanRemoveItem() => SelectedPreviewItem != null;

    [RelayCommand]
    private void SortItems()
    {
        var sorted = RenamePreviewItems
            .OrderBy(i => i.DetectedSeasonNumber)
            .ThenBy(i => i.DetectedEpisodeNumber)
            .ThenBy(i => i.OriginalFileName)
            .ToList();

        RenamePreviewItems.Clear();
        foreach (var item in sorted)
            RenamePreviewItems.Add(item);

        StatusText = "Items sorted by detected episode number";
    }

    [RelayCommand]
    private void ClearItems()
    {
        RenamePreviewItems.Clear();
        UpdateRenameCanExecute();
        StatusText = "All items cleared";
    }

    [RelayCommand(CanExecute = nameof(CanMoveItemUp))]
    private void MoveItemUp()
    {
        if (SelectedPreviewItem == null)
            return;

        var index = RenamePreviewItems.IndexOf(SelectedPreviewItem);
        if (index > 0)
        {
            RenamePreviewItems.Move(index, index - 1);
        }
    }

    private bool CanMoveItemUp() =>
        SelectedPreviewItem != null && RenamePreviewItems.IndexOf(SelectedPreviewItem) > 0;

    [RelayCommand(CanExecute = nameof(CanMoveItemDown))]
    private void MoveItemDown()
    {
        if (SelectedPreviewItem == null)
            return;

        var index = RenamePreviewItems.IndexOf(SelectedPreviewItem);
        if (index < RenamePreviewItems.Count - 1)
        {
            RenamePreviewItems.Move(index, index + 1);
        }
    }

    private bool CanMoveItemDown() =>
        SelectedPreviewItem != null && RenamePreviewItems.IndexOf(SelectedPreviewItem) < RenamePreviewItems.Count - 1;

    #endregion

    #region Auto-Match and Rename Commands

    [RelayCommand(CanExecute = nameof(CanAutoMatch))]
    private async Task AutoMatchAsync()
    {
        if (SelectedShow == null || RenamePreviewItems.Count == 0)
            return;

        StatusText = "Auto-matching files...";

        var allEpisodes = SelectedShow.Seasons.SelectMany(s => s.Episodes).ToList();
        var itemsToMatch = RenamePreviewItems.ToList();
        var showName = SelectedShow.Name;
        var format = SelectedNamingFormat;

        // Run matching on background thread to avoid UI freeze
        var results = await Task.Run(() =>
        {
            var matchResults = new List<(RenamePreviewItem Item, Episode? Episode)>();

            foreach (var item in itemsToMatch)
            {
                Episode? matchingEpisode = null;

                // First try to match by detected episode number
                if (item.DetectedSeasonNumber > 0 && item.DetectedEpisodeNumber > 0)
                {
                    matchingEpisode = allEpisodes.FirstOrDefault(e =>
                        e.SeasonNumber == item.DetectedSeasonNumber &&
                        e.EpisodeNumber == item.DetectedEpisodeNumber);
                }

                // If no number match, try name-based matching
                if (matchingEpisode == null)
                {
                    var (episode, confidence) = _fileMatchingService.MatchByName(item.OriginalFileName, allEpisodes);
                    if (episode != null && confidence != MatchConfidence.None)
                    {
                        matchingEpisode = episode;
                    }
                }

                matchResults.Add((item, matchingEpisode));
            }

            return matchResults;
        });

        // Apply results on UI thread
        var matchedCount = 0;
        foreach (var (item, episode) in results)
        {
            if (episode != null)
            {
                item.MatchedEpisode = episode;
                item.NewFileName = _fileMatchingService.GenerateFileName(episode, showName, format, item.Extension);
                item.Status = MatchStatus.Matched;
                matchedCount++;
            }
            else
            {
                item.MatchedEpisode = null;
                item.NewFileName = string.Empty;
                item.Status = MatchStatus.Unmatched;
            }
        }

        UpdateRenameCanExecute();
        StatusText = $"Auto-matched {matchedCount}/{RenamePreviewItems.Count} files";
    }

    private bool CanAutoMatch() =>
        SelectedShow != null && RenamePreviewItems.Count > 0;

    [RelayCommand]
    private void ClearAll()
    {
        RenamePreviewItems.Clear();
        UpdateRenameCanExecute();
        StatusText = "All items cleared";
    }

    [RelayCommand(CanExecute = nameof(CanRename))]
    private async Task Rename()
    {
        var itemsToRename = RenamePreviewItems.Where(i => i.HasMatch && i.Status != MatchStatus.Error).ToList();
        if (itemsToRename.Count == 0)
        {
            StatusText = "No matched items to rename";
            return;
        }

        IsRenaming = true;

        try
        {
            var renamed = 0;
            var skipped = 0;
            var failed = 0;

            await Task.Run(() =>
            {
                foreach (var item in itemsToRename)
                {
                    var newPath = Path.Combine(Path.GetDirectoryName(item.FilePath) ?? "", item.NewFileName);

                    // Skip if already renamed (same name)
                    if (string.Equals(item.FilePath, newPath, StringComparison.OrdinalIgnoreCase))
                    {
                        skipped++;
                        continue;
                    }

                    // Check if target exists
                    if (File.Exists(newPath))
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            item.Status = MatchStatus.Error;
                            item.ErrorMessage = "Target file already exists";
                        });
                        failed++;
                        continue;
                    }

                    // Check if source exists
                    if (!File.Exists(item.FilePath))
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            item.Status = MatchStatus.Error;
                            item.ErrorMessage = "Source file not found";
                        });
                        failed++;
                        continue;
                    }

                    try
                    {
                        File.Move(item.FilePath, newPath);
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            item.FilePath = newPath;
                            item.OriginalFileName = item.NewFileName;
                        });
                        renamed++;
                    }
                    catch (Exception ex)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            item.Status = MatchStatus.Error;
                            item.ErrorMessage = ex.Message;
                        });
                        failed++;
                    }
                }
            });

            var status = $"Renamed {renamed} files";
            if (skipped > 0) status += $", {skipped} already named correctly";
            if (failed > 0) status += $", {failed} failed";
            StatusText = status;
        }
        finally
        {
            IsRenaming = false;
        }
    }

    private bool CanRename() =>
        RenamePreviewItems.Any(i => i.HasMatch) && !IsRenaming;

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
            AddFilesToPreview(files);
        }
    }

    #endregion

    #region Property Change Handlers

    partial void OnSelectedShowChanged(TvShow? value)
    {
        // Update command states when show selection changes
        AutoMatchCommand.NotifyCanExecuteChanged();
        AssignEpisodeCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedSeasonChanged(Season? value)
    {
        OnPropertyChanged(nameof(CurrentSeasonEpisodes));
    }

    partial void OnSelectedPreviewItemChanged(RenamePreviewItem? value)
    {
        MoveItemUpCommand.NotifyCanExecuteChanged();
        MoveItemDownCommand.NotifyCanExecuteChanged();
        RemoveItemCommand.NotifyCanExecuteChanged();
        AssignEpisodeCommand.NotifyCanExecuteChanged();
        ClearAssignmentCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedBrowserEpisodeChanged(Episode? value)
    {
        AssignEpisodeCommand.NotifyCanExecuteChanged();
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

    private void AddFilesToPreview(IEnumerable<string> filePaths)
    {
        var added = 0;
        foreach (var filePath in filePaths)
        {
            // Don't add duplicates
            if (RenamePreviewItems.Any(i => i.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
                continue;

            var fileName = Path.GetFileName(filePath);
            var (season, episode, _) = _fileMatchingService.DetectEpisode(fileName);

            RenamePreviewItems.Add(new RenamePreviewItem
            {
                FilePath = filePath,
                OriginalFileName = fileName,
                DetectedSeasonNumber = season,
                DetectedEpisodeNumber = episode,
                Status = MatchStatus.Unmatched
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
/// Represents a row in the rename preview grid
/// </summary>
public partial class RenamePreviewItem : ObservableObject
{
    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _originalFileName = string.Empty;

    [ObservableProperty]
    private string _newFileName = string.Empty;

    [ObservableProperty]
    private Episode? _matchedEpisode;

    [ObservableProperty]
    private MatchStatus _status = MatchStatus.Unmatched;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private int _detectedSeasonNumber;

    [ObservableProperty]
    private int _detectedEpisodeNumber;

    /// <summary>
    /// Whether this item has a valid match
    /// </summary>
    public bool HasMatch => MatchedEpisode != null && !string.IsNullOrEmpty(NewFileName);

    /// <summary>
    /// Status icon for display
    /// </summary>
    public string StatusIcon => Status switch
    {
        MatchStatus.Matched => "✓",
        MatchStatus.Unmatched => "—",
        MatchStatus.Error => "✗",
        _ => ""
    };

    /// <summary>
    /// Tooltip text for the status column
    /// </summary>
    public string StatusTooltip => Status switch
    {
        MatchStatus.Matched => "Ready to rename",
        MatchStatus.Unmatched => "No episode matched",
        MatchStatus.Error => !string.IsNullOrEmpty(ErrorMessage) ? ErrorMessage : "Error",
        _ => ""
    };

    /// <summary>
    /// File extension
    /// </summary>
    public string Extension => Path.GetExtension(FilePath);
}

/// <summary>
/// Status of a rename preview item
/// </summary>
public enum MatchStatus
{
    Unmatched,
    Matched,
    Error
}
