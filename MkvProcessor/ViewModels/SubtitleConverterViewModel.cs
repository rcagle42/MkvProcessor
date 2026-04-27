using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MkvProcessor.Models;
using MkvProcessor.Models.Subtitle;
using MkvProcessor.Services;
using MkvProcessor.Services.Subtitle;
using MkvProcessor.Services.Subtitle.Strategies;
// Subtitle Edit integration was removed — it is a WinForms GUI app and cannot run truly
// headless, which broke the "self-contained" requirement. Orchestrator now relies on
// mkvextract (text/raw) + PgsToSrt (PGS OCR) exclusively.

namespace MkvProcessor.ViewModels;

/// <summary>
/// Supported OCR languages for subtitle conversion
/// </summary>
public class SubtitleLanguage
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public override string ToString() => $"{Name} ({Code})";
}

/// <summary>
/// View model for the Subtitle Converter tab
/// </summary>
public partial class SubtitleConverterViewModel : ObservableObject
{
    private readonly SettingsService _settingsService = new();
    private readonly PgsToSrtService _pgsToSrtService = new();
    private readonly MkvExtractService _mkvExtractService = new();
    private SubtitleOrchestrator? _orchestrator;
    private ProcessingSettings _settings = new();
    private CancellationTokenSource? _cancellationTokenSource;

    /// <summary>
    /// Lazily constructs the subtitle orchestrator. Shares the existing PgsToSrtService
    /// instance so its log and progress events continue to drive the tab's log panel.
    /// </summary>
    private SubtitleOrchestrator GetOrchestrator()
    {
        if (_orchestrator is not null)
            return _orchestrator;

        var strategies = new List<ISubtitleExtractionStrategy>
        {
            new TextPassthroughStrategy(_mkvExtractService),
            new PgsToSrtStrategy(_pgsToSrtService, _mkvExtractService),
            new VobSubEmbeddedOcrStrategy(_mkvExtractService),
        };
        _orchestrator = new SubtitleOrchestrator(strategies);
        _orchestrator.LogOutput += line =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                LogLines.Add("  " + line);
                while (LogLines.Count > 500)
                    LogLines.RemoveAt(0);
            });
        };
        return _orchestrator;
    }

    #region Observable Properties

    // === File Queue ===

    [ObservableProperty]
    private ObservableCollection<SubtitleFile> _files = [];

    [ObservableProperty]
    private SubtitleFile? _selectedFile;

    // === Processing State ===

    [ObservableProperty]
    private bool _isConverting;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private double _currentFileProgress;

    [ObservableProperty]
    private string _currentFileName = string.Empty;

    // === Settings ===

    [ObservableProperty]
    private string _pgsToSrtPath = string.Empty;

    [ObservableProperty]
    private string _tessdataPath = string.Empty;

    [ObservableProperty]
    private string _mkvExtractPath = string.Empty;

    [ObservableProperty]
    private bool _isSettingsExpanded;

    [ObservableProperty]
    private bool _isPgsToSrtAvailable;

    [ObservableProperty]
    private bool _isMkvExtractAvailable;

    [ObservableProperty]
    private int _subtitleMinAcceptableScore = 60;

    // === Compare Mode (Advanced Panel) ===

    /// <summary>
    /// When enabled, Convert() runs every applicable strategy (thorough mode) and leaves all
    /// viable candidate files on disk so the user can compare and override the winner.
    /// </summary>
    [ObservableProperty]
    private bool _compareMode;

    [ObservableProperty]
    private bool _isAdvancedExpanded;

    /// <summary>
    /// Candidates for the currently selected file in compare mode. Populated after each
    /// file is processed; the user can select one and click Use Selected to promote it.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<SubtitleCandidate> _currentCandidates = new();

    [ObservableProperty]
    private SubtitleCandidate? _selectedCandidate;

    /// <summary>
    /// Tracks the orchestration result per queued file so that clicking a file after
    /// conversion re-populates the Advanced panel with its candidates.
    /// </summary>
    private readonly Dictionary<SubtitleFile, SubtitleOrchestrationResult> _resultsByFile = new();

    partial void OnCompareModeChanged(bool value)
    {
        _settings.SubtitleCompareMode = value;
        if (value) IsAdvancedExpanded = true;
    }

    // === Language Selection ===

    [ObservableProperty]
    private ObservableCollection<SubtitleLanguage> _availableLanguages = [];

    [ObservableProperty]
    private SubtitleLanguage? _selectedLanguage;

    // === Status ===

    [ObservableProperty]
    private string _statusText = "Configure PgsToSrt path to get started";

    [ObservableProperty]
    private ObservableCollection<string> _logLines = [];

    #endregion

    /// <summary>
    /// Common languages available for OCR
    /// </summary>
    public static List<SubtitleLanguage> CommonLanguages { get; } =
    [
        new() { Code = "eng", Name = "English" },
        new() { Code = "spa", Name = "Spanish" },
        new() { Code = "fra", Name = "French" },
        new() { Code = "deu", Name = "German" },
        new() { Code = "ita", Name = "Italian" },
        new() { Code = "por", Name = "Portuguese" },
        new() { Code = "nld", Name = "Dutch" },
        new() { Code = "pol", Name = "Polish" },
        new() { Code = "rus", Name = "Russian" },
        new() { Code = "jpn", Name = "Japanese" },
        new() { Code = "kor", Name = "Korean" },
        new() { Code = "chi_sim", Name = "Chinese (Simplified)" },
        new() { Code = "chi_tra", Name = "Chinese (Traditional)" },
        new() { Code = "ara", Name = "Arabic" },
        new() { Code = "hin", Name = "Hindi" },
        new() { Code = "tha", Name = "Thai" },
        new() { Code = "vie", Name = "Vietnamese" },
        new() { Code = "swe", Name = "Swedish" },
        new() { Code = "nor", Name = "Norwegian" },
        new() { Code = "dan", Name = "Danish" },
        new() { Code = "fin", Name = "Finnish" },
    ];

    public SubtitleConverterViewModel()
    {
        // Wire up service events
        _pgsToSrtService.LogOutput += message =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                LogLines.Add(message);
                while (LogLines.Count > 500)
                    LogLines.RemoveAt(0);
            });
        };

        _pgsToSrtService.ProgressChanged += progress =>
        {
            Application.Current.Dispatcher.Invoke(() => CurrentFileProgress = progress);
        };

        // Initialize with common languages
        AvailableLanguages = new ObservableCollection<SubtitleLanguage>(CommonLanguages);
    }

    /// <summary>
    /// Initializes the view model (call after window loaded)
    /// </summary>
    public void Initialize()
    {
        // Load settings
        _settings = _settingsService.Load();
        PgsToSrtPath = _settings.PgsToSrtPath ?? string.Empty;
        TessdataPath = _settings.TessdataPath ?? string.Empty;
        MkvExtractPath = _settings.MkvExtractPath ?? string.Empty;
        SubtitleMinAcceptableScore = _settings.SubtitleMinAcceptableScore;

        // Set user-configured paths on each locator before the first availability check.
        if (!string.IsNullOrEmpty(PgsToSrtPath))
            PgsToSrtLocator.SetUserPath(PgsToSrtPath);
        if (!string.IsNullOrEmpty(MkvExtractPath))
            MkvExtractLocator.SetUserPath(MkvExtractPath);

        // Check availability of all tools
        UpdatePgsToSrtAvailability();
        UpdateToolAvailability();

        // Set default language
        var defaultLang = _settings.DefaultSubtitleLanguage ?? "eng";
        SelectedLanguage = AvailableLanguages.FirstOrDefault(l => l.Code == defaultLang)
            ?? AvailableLanguages.FirstOrDefault();

        // Update available languages from tessdata if path is set
        if (!string.IsNullOrEmpty(TessdataPath))
        {
            UpdateAvailableLanguages();
        }

        IsSettingsExpanded = !IsPgsToSrtAvailable;
    }

    private void UpdateToolAvailability()
    {
        IsMkvExtractAvailable = MkvExtractLocator.IsAvailable;
    }

    /// <summary>
    /// Saves current settings
    /// </summary>
    public void SaveSettings()
    {
        _settings.PgsToSrtPath = string.IsNullOrWhiteSpace(PgsToSrtPath) ? null : PgsToSrtPath;
        _settings.TessdataPath = string.IsNullOrWhiteSpace(TessdataPath) ? null : TessdataPath;
        _settings.MkvExtractPath = string.IsNullOrWhiteSpace(MkvExtractPath) ? null : MkvExtractPath;
        _settings.DefaultSubtitleLanguage = SelectedLanguage?.Code ?? "eng";
        _settings.SubtitleMinAcceptableScore = SubtitleMinAcceptableScore;
        _settingsService.Save(_settings);
    }

    #region File Commands

    [RelayCommand]
    private void AddFiles()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select subtitle files",
            Multiselect = true,
            Filter = "SUP Files|*.sup|All Files|*.*",
            InitialDirectory = _settings.LastSubtitleFolder
        };

        if (dialog.ShowDialog() == true)
        {
            AddFilesToQueue(dialog.FileNames);
            _settings.LastSubtitleFolder = Path.GetDirectoryName(dialog.FileNames.FirstOrDefault());
            SaveSettings();
        }
    }

    [RelayCommand]
    private void AddFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select folder containing .sup files",
            InitialDirectory = _settings.LastSubtitleFolder
        };

        if (dialog.ShowDialog() == true)
        {
            var files = Directory.GetFiles(dialog.FolderName, "*.sup", SearchOption.AllDirectories);
            AddFilesToQueue(files);
            _settings.LastSubtitleFolder = dialog.FolderName;
            SaveSettings();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRemoveFile))]
    private void RemoveFile()
    {
        if (SelectedFile != null)
        {
            Files.Remove(SelectedFile);
            UpdateCommandStates();
        }
    }

    private bool CanRemoveFile() => SelectedFile != null && !IsConverting;

    [RelayCommand]
    private void ClearFiles()
    {
        Files.Clear();
        UpdateCommandStates();
        StatusText = "Queue cleared";
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var file in Files)
            file.IsSelected = true;
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var file in Files)
            file.IsSelected = false;
    }

    #endregion

    #region Processing Commands

    [RelayCommand(CanExecute = nameof(CanConvert))]
    private async Task Convert()
    {
        if (!IsPgsToSrtAvailable)
        {
            StatusText = "PgsToSrt not configured";
            return;
        }

        if (SelectedLanguage == null)
        {
            StatusText = "Please select a language";
            return;
        }

        IsConverting = true;
        IsPaused = false;
        _cancellationTokenSource = new CancellationTokenSource();
        LogLines.Clear();

        var filesToConvert = Files
            .Where(f => f.IsSelected && f.Status == SubtitleConversionStatus.Pending)
            .ToList();

        var completed = 0;
        var failed = 0;
        var startTime = DateTime.Now;

        try
        {
            StatusText = $"Converting 0/{filesToConvert.Count}...";
            LogLines.Add($"=== Starting conversion of {filesToConvert.Count} files ===");
            LogLines.Add($"Language: {SelectedLanguage.Name} ({SelectedLanguage.Code})");

            foreach (var file in filesToConvert)
            {
                // Wait while paused
                while (IsPaused && !_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    await Task.Delay(100, _cancellationTokenSource.Token);
                }

                _cancellationTokenSource.Token.ThrowIfCancellationRequested();

                CurrentFileName = file.FileName;
                CurrentFileProgress = 0;
                file.Status = SubtitleConversionStatus.Converting;
                file.StatusText = "Converting...";

                // Route the conversion through the orchestrator. For standalone files the
                // descriptor is built from the path + chosen language, and the orchestrator
                // decides which strategies can handle it.
                var descriptor = SubtitleSourceDescriptor.FromStandaloneFile(file.FilePath, SelectedLanguage.Code);
                var outputDir = Path.GetDirectoryName(file.FilePath) ?? ".";
                var baseName = Path.GetFileNameWithoutExtension(file.FilePath);

                var context = new SubtitleScoringContext(
                    ExpectedLanguage: SelectedLanguage.Code,
                    VideoDuration: null,
                    SourceClass: descriptor.CodecClass);

                SubtitleOrchestrationResult orchResult;
                try
                {
                    orchResult = await GetOrchestrator().ExtractAsync(
                        descriptor,
                        outputDir,
                        baseName,
                        SelectedLanguage.Code,
                        string.IsNullOrEmpty(TessdataPath) ? null : TessdataPath,
                        context,
                        _settings.SubtitleMinAcceptableScore,
                        thoroughMode: _settings.SubtitleCompareMode,
                        _cancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    file.Status = SubtitleConversionStatus.Error;
                    file.StatusText = "Error";
                    file.ErrorMessage = ex.Message;
                    failed++;
                    StatusText = $"Converting {completed + failed}/{filesToConvert.Count}...";
                    continue;
                }

                if (orchResult.HasWinner)
                {
                    // Remember the full result so the Advanced panel can re-populate on selection.
                    _resultsByFile[file] = orchResult;

                    string finalPath;
                    if (CompareMode)
                    {
                        // In compare mode, leave every viable candidate on disk so the user
                        // can inspect and override the auto-selected winner. file.OutputPath
                        // points at the top-scored candidate until the user clicks Use Selected.
                        finalPath = orchResult.Winner!.FilePath;
                    }
                    else
                    {
                        // Fast mode: immediately promote the winner to the canonical name and
                        // delete losing candidates.
                        finalPath = file.ExpectedOutputPath;
                        var winnerPath = orchResult.Winner!.FilePath;
                        if (!string.Equals(winnerPath, finalPath, StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                if (File.Exists(finalPath)) File.Delete(finalPath);
                                File.Move(winnerPath, finalPath);
                            }
                            catch
                            {
                                finalPath = winnerPath;
                            }
                        }

                        foreach (var c in orchResult.AllCandidates)
                        {
                            if (!c.IsViable) continue;
                            if (string.Equals(c.FilePath, finalPath, StringComparison.OrdinalIgnoreCase)) continue;
                            try { if (File.Exists(c.FilePath)) File.Delete(c.FilePath); } catch { }
                        }
                    }

                    file.Status = SubtitleConversionStatus.Complete;
                    file.StatusText = $"Complete ({orchResult.Winner.StrategyName}, score {orchResult.Winner.Score})";
                    file.OutputPath = finalPath;
                    file.Progress = 100;
                    completed++;

                    // Refresh the Advanced panel if this file is currently selected.
                    if (ReferenceEquals(SelectedFile, file))
                        OnSelectedFileChanged(file);
                }
                else
                {
                    var reason = orchResult.AllCandidates
                        .SelectMany(c => c.Issues)
                        .FirstOrDefault() ?? "No strategy produced a viable subtitle";
                    file.Status = SubtitleConversionStatus.Error;
                    file.StatusText = "Error";
                    file.ErrorMessage = reason;
                    failed++;
                }

                StatusText = $"Converting {completed + failed}/{filesToConvert.Count}...";
            }

            var duration = DateTime.Now - startTime;
            StatusText = $"Complete: {completed} converted, {failed} failed ({duration.TotalSeconds:F1}s)";
            LogLines.Add($"=== Conversion complete: {completed} success, {failed} failed ===");
        }
        catch (OperationCanceledException)
        {
            StatusText = "Conversion cancelled";
            LogLines.Add("=== Conversion cancelled ===");
        }
        finally
        {
            IsConverting = false;
            IsPaused = false;
            CurrentFileName = string.Empty;
            CurrentFileProgress = 0;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            UpdateCommandStates();
        }
    }

    private bool CanConvert() =>
        !IsConverting &&
        IsPgsToSrtAvailable &&
        Files.Any(f => f.IsSelected && f.Status == SubtitleConversionStatus.Pending);

    [RelayCommand(CanExecute = nameof(CanPause))]
    private void Pause()
    {
        IsPaused = !IsPaused;
        StatusText = IsPaused ? "Paused" : "Resuming...";
    }

    private bool CanPause() => IsConverting;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cancellationTokenSource?.Cancel();
        StatusText = "Cancelling...";
    }

    private bool CanCancel() => IsConverting;

    #endregion

    #region Settings Commands

    [RelayCommand]
    private void BrowsePgsToSrtPath()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select PgsToSrt.dll",
            Filter = "DLL Files|*.dll|All Files|*.*",
            FileName = "PgsToSrt.dll"
        };

        if (dialog.ShowDialog() == true)
        {
            PgsToSrtPath = dialog.FileName;
            PgsToSrtLocator.SetUserPath(PgsToSrtPath);
            UpdatePgsToSrtAvailability();
            SaveSettings();
        }
    }

    [RelayCommand]
    private void BrowseTessdataPath()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select tessdata folder (containing .traineddata files)"
        };

        if (dialog.ShowDialog() == true)
        {
            TessdataPath = dialog.FolderName;
            UpdateAvailableLanguages();
            SaveSettings();
        }
    }

    /// <summary>
    /// Promotes the currently selected candidate from the Advanced panel to the canonical
    /// Plex-compatible filename, replacing whatever was auto-selected. Deletes all other
    /// viable candidates. Only meaningful after a compare-mode conversion has left
    /// candidates on disk.
    /// </summary>
    [RelayCommand]
    private void UseSelectedCandidate()
    {
        if (SelectedFile is null || SelectedCandidate is null || !SelectedCandidate.IsViable)
            return;

        if (!_resultsByFile.TryGetValue(SelectedFile, out var result))
            return;

        var finalPath = SelectedFile.ExpectedOutputPath;
        var chosenPath = SelectedCandidate.FilePath;

        try
        {
            if (!string.Equals(chosenPath, finalPath, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(finalPath)) File.Delete(finalPath);
                File.Copy(chosenPath, finalPath);
            }

            // Clean up all other viable candidates.
            foreach (var c in result.AllCandidates)
            {
                if (!c.IsViable) continue;
                if (string.Equals(c.FilePath, finalPath, StringComparison.OrdinalIgnoreCase)) continue;
                try { if (File.Exists(c.FilePath)) File.Delete(c.FilePath); } catch { }
            }

            SelectedFile.OutputPath = finalPath;
            SelectedFile.StatusText =
                $"Complete ({SelectedCandidate.StrategyName}, score {SelectedCandidate.Score}) — user override";

            // The other candidate files no longer exist; refresh the panel with just the winner.
            CurrentCandidates.Clear();
            CurrentCandidates.Add(SelectedCandidate);
            StatusText = $"Promoted {SelectedCandidate.StrategyName} for {SelectedFile.FileName}";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to promote candidate: {ex.Message}";
        }
    }

    [RelayCommand]
    private void BrowseMkvExtractPath()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select mkvextract.exe (from MKVToolNix)",
            Filter = "Executable|mkvextract.exe|All Files|*.*",
            FileName = "mkvextract.exe"
        };

        if (dialog.ShowDialog() == true)
        {
            MkvExtractPath = dialog.FileName;
            MkvExtractLocator.SetUserPath(MkvExtractPath);
            UpdateToolAvailability();
            SaveSettings();
            // Force orchestrator to pick up the new locator on next run
            _orchestrator = null;
        }
    }

    [RelayCommand]
    private void SaveSettingsCommand()
    {
        PgsToSrtLocator.SetUserPath(string.IsNullOrWhiteSpace(PgsToSrtPath) ? null : PgsToSrtPath);
        MkvExtractLocator.SetUserPath(string.IsNullOrWhiteSpace(MkvExtractPath) ? null : MkvExtractPath);
        UpdatePgsToSrtAvailability();
        UpdateToolAvailability();
        _orchestrator = null; // rebuild on next run with fresh locators
        SaveSettings();
        StatusText = "Settings saved";
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
                files.AddRange(Directory.GetFiles(path, "*.sup", SearchOption.AllDirectories));
            }
            else if (File.Exists(path) && path.EndsWith(".sup", StringComparison.OrdinalIgnoreCase))
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

    partial void OnSelectedFileChanged(SubtitleFile? value)
    {
        RemoveFileCommand.NotifyCanExecuteChanged();

        // Re-populate the Advanced / Compare panel with this file's candidates.
        CurrentCandidates.Clear();
        SelectedCandidate = null;
        if (value is not null && _resultsByFile.TryGetValue(value, out var result))
        {
            foreach (var c in result.AllCandidates.Where(c => c.IsViable))
                CurrentCandidates.Add(c);
            SelectedCandidate = result.Winner;
        }
    }

    partial void OnIsConvertingChanged(bool value)
    {
        UpdateCommandStates();
    }

    #endregion

    #region Private Methods

    private void AddFilesToQueue(IEnumerable<string> filePaths)
    {
        var added = 0;
        foreach (var filePath in filePaths)
        {
            // Don't add duplicates
            if (Files.Any(f => f.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
                continue;

            var fileInfo = new FileInfo(filePath);
            Files.Add(new SubtitleFile
            {
                FilePath = filePath,
                FileName = fileInfo.Name,
                FileSize = fileInfo.Exists ? fileInfo.Length : 0
            });
            added++;
        }

        UpdateCommandStates();
        StatusText = $"Added {added} files ({Files.Count} total)";
    }

    private void UpdatePgsToSrtAvailability()
    {
        PgsToSrtLocator.Refresh();
        IsPgsToSrtAvailable = PgsToSrtLocator.IsAvailable;

        if (IsPgsToSrtAvailable)
        {
            StatusText = "Ready";
            IsSettingsExpanded = false;

            // Try to auto-detect tessdata if not set
            if (string.IsNullOrEmpty(TessdataPath))
            {
                var autoTessdata = PgsToSrtLocator.FindTessdata();
                if (!string.IsNullOrEmpty(autoTessdata))
                {
                    TessdataPath = autoTessdata;
                    UpdateAvailableLanguages();
                }
            }
        }
        else
        {
            StatusText = "PgsToSrt not found - configure path in settings";
            IsSettingsExpanded = true;
        }
    }

    private void UpdateAvailableLanguages()
    {
        if (string.IsNullOrEmpty(TessdataPath) || !Directory.Exists(TessdataPath))
        {
            // Keep common languages if tessdata not available
            return;
        }

        var detectedLanguages = _pgsToSrtService.GetAvailableLanguages(TessdataPath);
        if (detectedLanguages.Count > 0)
        {
            var languages = new List<SubtitleLanguage>();
            foreach (var code in detectedLanguages)
            {
                var known = CommonLanguages.FirstOrDefault(l => l.Code == code);
                languages.Add(known ?? new SubtitleLanguage { Code = code, Name = code });
            }

            AvailableLanguages = new ObservableCollection<SubtitleLanguage>(
                languages.OrderBy(l => l.Name));

            // Maintain selection
            var currentCode = SelectedLanguage?.Code ?? _settings.DefaultSubtitleLanguage ?? "eng";
            SelectedLanguage = AvailableLanguages.FirstOrDefault(l => l.Code == currentCode)
                ?? AvailableLanguages.FirstOrDefault();
        }
    }

    private void UpdateCommandStates()
    {
        ConvertCommand.NotifyCanExecuteChanged();
        PauseCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        RemoveFileCommand.NotifyCanExecuteChanged();
    }

    #endregion
}
