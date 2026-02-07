using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MkvProcessor.Models;
using MkvProcessor.Services;

namespace MkvProcessor.ViewModels;

/// <summary>
/// Main view model for the application
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly SettingsService _settingsService = new();
    private readonly MediaInfoService _mediaInfoService = new();
    private readonly EncoderDetectionService _encoderDetectionService = new();
    private readonly ProcessingQueue _processingQueue = new();

    #region Observable Properties

    [ObservableProperty]
    private ProcessingSettings _settings = new();

    [ObservableProperty]
    private ObservableCollection<EncoderInfo> _availableEncoders = [];

    [ObservableProperty]
    private EncoderInfo? _selectedEncoder;

    [ObservableProperty]
    private ObservableCollection<QualityPreset> _qualityPresets = [];

    [ObservableProperty]
    private QualityPreset? _selectedQualityPreset;

    [ObservableProperty]
    private string _inputFolder = string.Empty;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private double _currentFileProgress;

    [ObservableProperty]
    private double _overallProgress;

    [ObservableProperty]
    private string _currentFileName = string.Empty;

    [ObservableProperty]
    private string _currentStep = string.Empty;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private ObservableCollection<string> _logLines = [];

    [ObservableProperty]
    private string _logText = string.Empty;

    [ObservableProperty]
    private bool _isSettingsExpanded = true;

    [ObservableProperty]
    private bool _isLogExpanded = true;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string _selectedFilesInfo = string.Empty;

    #endregion

    /// <summary>
    /// Files in the processing queue
    /// </summary>
    public ObservableCollection<MkvFile> Files => _processingQueue.Files;

    /// <summary>
    /// Audio mode options for the dropdown
    /// </summary>
    public AudioMode[] AudioModes => [AudioMode.Dual, AudioMode.Original, AudioMode.Normalized];

    /// <summary>
    /// Subtitle language options for the filter dropdown
    /// </summary>
    public string[] SubtitleLanguages => ["All", "eng", "spa", "fra", "deu", "ita", "por", "jpn", "kor", "chi", "rus", "ara", "nld", "pol"];

    public MainViewModel()
    {
        // Wire up events
        _processingQueue.IsProcessingChanged += isProcessing =>
        {
            Application.Current.Dispatcher.Invoke(() => IsProcessing = isProcessing);
        };

        _processingQueue.IsPausedChanged += isPaused =>
        {
            Application.Current.Dispatcher.Invoke(() => IsPaused = isPaused);
        };

        _processingQueue.ProgressChanged += progress =>
        {
            Application.Current.Dispatcher.Invoke(() => CurrentFileProgress = progress);
        };

        _processingQueue.StepChanged += step =>
        {
            Application.Current.Dispatcher.Invoke(() => CurrentStep = step);
        };

        _processingQueue.LogOutput += line =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                LogLines.Add(line);
                // Keep log size manageable
                while (LogLines.Count > 1000)
                    LogLines.RemoveAt(0);
                // Update the text property for the TextBox binding
                LogText = string.Join(Environment.NewLine, LogLines);
            });
        };

        _processingQueue.FileStarted += file =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                CurrentFileName = file.FileName;
                CurrentFileProgress = 0;
                UpdateOverallProgress();
            });
        };

        _processingQueue.FileCompleted += result =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                UpdateOverallProgress();
                UpdateSelectedFilesInfo();
            });
        };

        _processingQueue.ProcessingCompleted += summary =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                CurrentFileName = string.Empty;
                CurrentStep = string.Empty;
                StatusText = $"Complete: {summary.SuccessfulFiles} succeeded, {summary.FailedFiles} failed, {summary.SkippedFiles} skipped";
                OnProcessingCompleted?.Invoke(summary);
            });
        };

        // Subscribe to file selection changes
        Files.CollectionChanged += (_, _) => UpdateSelectedFilesInfo();
    }

    /// <summary>
    /// Event raised when processing completes (for tray notification)
    /// </summary>
    public event Action<ProcessingSummary>? OnProcessingCompleted;

    /// <summary>
    /// Initializes the view model (call after window loaded)
    /// </summary>
    public async Task InitializeAsync()
    {
        IsLoading = true;

        // Load settings
        Settings = _settingsService.Load();
        InputFolder = Settings.LastInputFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

        // Load quality presets
        UpdateQualityPresets();

        // Detect encoders
        var encoders = await _encoderDetectionService.DetectAvailableEncodersAsync();
        AvailableEncoders = new ObservableCollection<EncoderInfo>(encoders);

        // Select saved encoder or best available
        var savedEncoder = encoders.FirstOrDefault(e => e.Type == Settings.Encoder);
        if (savedEncoder != null)
        {
            SelectedEncoder = savedEncoder;
        }
        else
        {
            // Prefer hardware encoders
            SelectedEncoder = encoders.FirstOrDefault(e => e.Type == EncoderType.Nvenc && e.IsAvailable)
                ?? encoders.FirstOrDefault(e => e.Type == EncoderType.Amf && e.IsAvailable)
                ?? encoders.FirstOrDefault(e => e.Type == EncoderType.Qsv && e.IsAvailable)
                ?? encoders.First();
        }

        IsLoading = false;
        StatusText = $"Ready - {AvailableEncoders.Count(e => e.IsAvailable)} encoder(s) available";
    }

    #region Commands

    [RelayCommand]
    private async Task BrowseInputFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select folder containing MKV files",
            InitialDirectory = Directory.Exists(InputFolder) ? InputFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
        };

        if (dialog.ShowDialog() == true)
        {
            InputFolder = dialog.FolderName;
            Settings.LastInputFolder = InputFolder;
            await LoadFilesFromFolderAsync(InputFolder);
        }
    }

    [RelayCommand]
    private async Task AddFiles()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select MKV files",
            Filter = "MKV Files (*.mkv)|*.mkv|All Files (*.*)|*.*",
            Multiselect = true,
            InitialDirectory = Directory.Exists(InputFolder) ? InputFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
        };

        if (dialog.ShowDialog() == true && dialog.FileNames.Length > 0)
        {
            await AddDroppedItemsAsync(dialog.FileNames);
        }
    }

    [RelayCommand]
    private async Task AddFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select folder containing MKV files",
            InitialDirectory = Directory.Exists(InputFolder) ? InputFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
        };

        if (dialog.ShowDialog() == true)
        {
            await AddDroppedItemsAsync([dialog.FolderName]);
        }
    }

    [RelayCommand]
    private void BrowseFileOutputFolder(MkvFile? file)
    {
        if (file == null) return;

        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select output folder for this file",
            InitialDirectory = Directory.Exists(file.OutputFolder)
                ? file.OutputFolder
                : Path.GetDirectoryName(file.FilePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
        };

        if (dialog.ShowDialog() == true)
        {
            file.OutputFolder = dialog.FolderName;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartProcessing))]
    private async Task StartProcessing()
    {
        SaveSettings();
        StatusText = "Processing...";
        LogLines.Clear();
        LogText = string.Empty;
        await _processingQueue.StartProcessingAsync(Settings);
    }

    private bool CanStartProcessing() =>
        !IsProcessing && _processingQueue.GetSelectedPendingCount() > 0;

    [RelayCommand(CanExecute = nameof(CanStartProcessing))]
    private async Task ExtractSubtitles()
    {
        SaveSettings();
        StatusText = "Extracting subtitles...";
        LogLines.Clear();
        LogText = string.Empty;
        await _processingQueue.ExtractSubtitlesAsync(Settings);
    }

    [RelayCommand(CanExecute = nameof(CanPause))]
    private void PauseProcessing()
    {
        _processingQueue.Pause();
        StatusText = "Paused";
    }

    private bool CanPause() => IsProcessing && !IsPaused;

    [RelayCommand(CanExecute = nameof(CanResume))]
    private void ResumeProcessing()
    {
        _processingQueue.Resume();
        StatusText = "Processing...";
    }

    private bool CanResume() => IsProcessing && IsPaused;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void CancelProcessing()
    {
        _processingQueue.Cancel();
        StatusText = "Cancelling...";
    }

    private bool CanCancel() => IsProcessing;

    [RelayCommand]
    private void RemoveSelectedFile(MkvFile? file)
    {
        if (file != null)
        {
            _processingQueue.RemoveFile(file);
            UpdateSelectedFilesInfo();
        }
    }

    [RelayCommand]
    private void MoveFileUp(MkvFile? file)
    {
        if (file != null)
            _processingQueue.MoveUp(file);
    }

    [RelayCommand]
    private void MoveFileDown(MkvFile? file)
    {
        if (file != null)
            _processingQueue.MoveDown(file);
    }

    [RelayCommand]
    private void ClearCompleted()
    {
        _processingQueue.RemoveCompleted();
        UpdateSelectedFilesInfo();
    }

    [RelayCommand]
    private void ClearAll()
    {
        _processingQueue.Clear();
        UpdateSelectedFilesInfo();
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var file in Files)
            file.IsSelected = true;
        UpdateSelectedFilesInfo();
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var file in Files)
            file.IsSelected = false;
        UpdateSelectedFilesInfo();
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Adds files from a drag-drop operation
    /// </summary>
    public async Task AddDroppedItemsAsync(string[] paths)
    {
        var mkvFiles = new List<string>();

        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                mkvFiles.AddRange(_mediaInfoService.GetMkvFilesInDirectory(path));
                if (string.IsNullOrEmpty(InputFolder) || !Directory.Exists(InputFolder))
                {
                    InputFolder = path;
                    Settings.LastInputFolder = path;
                }
            }
            else if (File.Exists(path) && path.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase))
            {
                mkvFiles.Add(path);
                var folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder) && (string.IsNullOrEmpty(InputFolder) || !Directory.Exists(InputFolder)))
                {
                    InputFolder = folder;
                    Settings.LastInputFolder = folder;
                }
            }
        }

        if (mkvFiles.Count > 0)
        {
            StatusText = $"Loading {mkvFiles.Count} file(s)...";
            var newFiles = await _mediaInfoService.GetFilesInfoAsync(mkvFiles);

            // Only add files not already in the queue
            var existingPaths = Files.Select(f => f.FilePath).ToHashSet();
            foreach (var file in newFiles.Where(f => !existingPaths.Contains(f.FilePath)))
            {
                _processingQueue.AddFile(file);
            }

            StatusText = $"{Files.Count} file(s) in queue";
            UpdateSelectedFilesInfo();
        }
    }

    /// <summary>
    /// Loads files from a folder
    /// </summary>
    public async Task LoadFilesFromFolderAsync(string folderPath)
    {
        var mkvFiles = _mediaInfoService.GetMkvFilesInDirectory(folderPath).ToList();
        if (mkvFiles.Count == 0)
        {
            StatusText = "No MKV files found in folder";
            return;
        }

        StatusText = $"Loading {mkvFiles.Count} file(s)...";
        _processingQueue.Clear();
        var files = await _mediaInfoService.GetFilesInfoAsync(mkvFiles);
        _processingQueue.AddFiles(files);

        StatusText = $"{Files.Count} file(s) loaded";
        UpdateSelectedFilesInfo();
    }

    /// <summary>
    /// Saves current settings
    /// </summary>
    public void SaveSettings()
    {
        if (SelectedEncoder != null)
            Settings.Encoder = SelectedEncoder.Type;

        Settings.QualityPresetIndex = Array.IndexOf(QualityPreset.Presets, SelectedQualityPreset);

        _settingsService.Save(Settings);
    }

    #endregion

    #region Property Change Handlers

    partial void OnSettingsChanged(ProcessingSettings value)
    {
        UpdateQualityPresets();
    }

    partial void OnSelectedEncoderChanged(EncoderInfo? value)
    {
        if (value != null)
            Settings.Encoder = value.Type;
    }

    partial void OnSelectedQualityPresetChanged(QualityPreset? value)
    {
        if (value != null)
        {
            Settings.QualityPresetIndex = Array.IndexOf(QualityPreset.Presets, value);
        }
    }

    #endregion

    #region Private Methods

    private void UpdateQualityPresets()
    {
        var presets = QualityPreset.Presets;

        QualityPresets = new ObservableCollection<QualityPreset>(presets);

        var index = Math.Clamp(Settings.QualityPresetIndex, 0, presets.Length - 1);
        SelectedQualityPreset = presets[index];
    }

    private void UpdateOverallProgress()
    {
        var total = Files.Count(f => f.IsSelected);
        if (total == 0)
        {
            OverallProgress = 0;
            return;
        }

        var completed = Files.Count(f => f.IsSelected && f.Status is FileStatus.Complete or FileStatus.Skipped or FileStatus.Error);
        OverallProgress = (double)completed / total * 100;
    }

    private void UpdateSelectedFilesInfo()
    {
        var count = _processingQueue.GetSelectedPendingCount();
        var size = _processingQueue.GetSelectedPendingSize();
        var sizeFormatted = FormatFileSize(size);
        SelectedFilesInfo = $"{count} selected, {sizeFormatted}";

        // Notify command can execute changed
        StartProcessingCommand.NotifyCanExecuteChanged();
        ExtractSubtitlesCommand.NotifyCanExecuteChanged();
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }

    #endregion
}
