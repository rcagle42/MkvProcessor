using System.Collections.ObjectModel;
using MkvProcessor.Models;

namespace MkvProcessor.Services;

/// <summary>
/// Manages the queue of files to be processed
/// </summary>
public class ProcessingQueue
{
    private readonly FFmpegService _ffmpegService = new();
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _isPaused;

    /// <summary>
    /// The queue of files to process
    /// </summary>
    public ObservableCollection<MkvFile> Files { get; } = [];

    /// <summary>
    /// Whether processing is currently paused
    /// </summary>
    public bool IsPaused
    {
        get => _isPaused;
        private set
        {
            _isPaused = value;
            IsPausedChanged?.Invoke(value);
        }
    }

    /// <summary>
    /// Whether processing is currently running
    /// </summary>
    public bool IsProcessing { get; private set; }

    /// <summary>
    /// The file currently being processed
    /// </summary>
    public MkvFile? CurrentFile { get; private set; }

    /// <summary>
    /// Raised when a file starts processing
    /// </summary>
    public event Action<MkvFile>? FileStarted;

    /// <summary>
    /// Raised when a file finishes processing
    /// </summary>
    public event Action<ProcessingResult>? FileCompleted;

    /// <summary>
    /// Raised when all processing is complete
    /// </summary>
    public event Action<ProcessingSummary>? ProcessingCompleted;

    /// <summary>
    /// Raised when processing progress changes
    /// </summary>
    public event Action<double>? ProgressChanged;

    /// <summary>
    /// Raised when log output is received
    /// </summary>
    public event Action<string>? LogOutput;

    /// <summary>
    /// Raised when current step changes
    /// </summary>
    public event Action<string>? StepChanged;

    /// <summary>
    /// Raised when IsProcessing changes
    /// </summary>
    public event Action<bool>? IsProcessingChanged;

    /// <summary>
    /// Raised when IsPaused changes
    /// </summary>
    public event Action<bool>? IsPausedChanged;

    public ProcessingQueue()
    {
        _ffmpegService.LogOutput += line => LogOutput?.Invoke(line);
        _ffmpegService.ProgressChanged += progress => ProgressChanged?.Invoke(progress);
        _ffmpegService.StepChanged += step => StepChanged?.Invoke(step);
    }

    /// <summary>
    /// Adds a file to the queue
    /// </summary>
    public void AddFile(MkvFile file)
    {
        Files.Add(file);
    }

    /// <summary>
    /// Adds multiple files to the queue
    /// </summary>
    public void AddFiles(IEnumerable<MkvFile> files)
    {
        foreach (var file in files)
        {
            Files.Add(file);
        }
    }

    /// <summary>
    /// Removes a file from the queue
    /// </summary>
    public void RemoveFile(MkvFile file)
    {
        Files.Remove(file);
    }

    /// <summary>
    /// Clears all files from the queue
    /// </summary>
    public void Clear()
    {
        Files.Clear();
    }

    /// <summary>
    /// Removes completed and skipped files from the queue
    /// </summary>
    public void RemoveCompleted()
    {
        var toRemove = Files.Where(f => f.Status is FileStatus.Complete or FileStatus.Skipped).ToList();
        foreach (var file in toRemove)
        {
            Files.Remove(file);
        }
    }

    /// <summary>
    /// Moves a file up in the queue
    /// </summary>
    public void MoveUp(MkvFile file)
    {
        var index = Files.IndexOf(file);
        if (index > 0)
        {
            Files.Move(index, index - 1);
        }
    }

    /// <summary>
    /// Moves a file down in the queue
    /// </summary>
    public void MoveDown(MkvFile file)
    {
        var index = Files.IndexOf(file);
        if (index >= 0 && index < Files.Count - 1)
        {
            Files.Move(index, index + 1);
        }
    }

    /// <summary>
    /// Starts processing the queue
    /// </summary>
    public async Task StartProcessingAsync(ProcessingSettings settings)
    {
        if (IsProcessing)
            return;

        IsProcessing = true;
        IsProcessingChanged?.Invoke(true);
        IsPaused = false;
        _cancellationTokenSource = new CancellationTokenSource();

        var summary = new ProcessingSummary();
        var startTime = DateTime.Now;

        try
        {
            LogOutput?.Invoke($"=== Starting Processing ===");
            LogOutput?.Invoke($"Total files in queue: {Files.Count}");

            foreach (var f in Files)
            {
                LogOutput?.Invoke($"  - {f.FileName}: Selected={f.IsSelected}, Status={f.Status}");
            }

            var filesToProcess = Files
                .Where(f => f.IsSelected && f.Status == FileStatus.Pending)
                .ToList();

            LogOutput?.Invoke($"Files to process (selected + pending): {filesToProcess.Count}");
            summary.TotalFiles = filesToProcess.Count;

            if (filesToProcess.Count == 0)
            {
                LogOutput?.Invoke("No files to process! Check that files are selected and have Pending status.");
            }

            foreach (var file in filesToProcess)
            {
                // Wait while paused
                while (IsPaused && !_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    await Task.Delay(100, _cancellationTokenSource.Token);
                }

                _cancellationTokenSource.Token.ThrowIfCancellationRequested();

                CurrentFile = file;
                FileStarted?.Invoke(file);

                var result = await _ffmpegService.ProcessFileAsync(
                    file, settings, _cancellationTokenSource.Token);

                FileCompleted?.Invoke(result);

                if (result.Skipped)
                    summary.SkippedFiles++;
                else if (result.Success)
                    summary.SuccessfulFiles++;
                else
                    summary.FailedFiles++;
            }
        }
        catch (OperationCanceledException)
        {
            LogOutput?.Invoke("Processing cancelled by user");
        }
        finally
        {
            summary.TotalTime = DateTime.Now - startTime;
            CurrentFile = null;
            IsProcessing = false;
            IsProcessingChanged?.Invoke(false);
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

            ProcessingCompleted?.Invoke(summary);
        }
    }

    /// <summary>
    /// Extracts subtitles from selected files without encoding
    /// </summary>
    public async Task ExtractSubtitlesAsync(ProcessingSettings settings)
    {
        if (IsProcessing)
            return;

        IsProcessing = true;
        IsProcessingChanged?.Invoke(true);
        IsPaused = false;
        _cancellationTokenSource = new CancellationTokenSource();

        var summary = new ProcessingSummary();
        var startTime = DateTime.Now;

        try
        {
            LogOutput?.Invoke($"=== Starting Subtitle Extraction ===");

            var filesToProcess = Files
                .Where(f => f.IsSelected && f.Status == FileStatus.Pending)
                .ToList();

            LogOutput?.Invoke($"Files to extract subtitles from: {filesToProcess.Count}");
            summary.TotalFiles = filesToProcess.Count;

            if (filesToProcess.Count == 0)
            {
                LogOutput?.Invoke("No files to process! Check that files are selected and have Pending status.");
            }

            foreach (var file in filesToProcess)
            {
                while (IsPaused && !_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    await Task.Delay(100, _cancellationTokenSource.Token);
                }

                _cancellationTokenSource.Token.ThrowIfCancellationRequested();

                CurrentFile = file;
                file.Status = FileStatus.Processing;
                file.CurrentStep = "Extracting subtitles";
                FileStarted?.Invoke(file);

                try
                {
                    await _ffmpegService.ExtractSubtitlesOnlyAsync(
                        file, settings.SubtitleLanguageFilter, _cancellationTokenSource.Token);

                    file.Status = FileStatus.Complete;
                    file.Progress = 100;
                    summary.SuccessfulFiles++;
                    FileCompleted?.Invoke(ProcessingResult.Successful(file, file.FilePath, 0, TimeSpan.Zero));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    file.Status = FileStatus.Error;
                    file.ErrorMessage = ex.Message;
                    summary.FailedFiles++;
                    FileCompleted?.Invoke(ProcessingResult.Failed(file, ex.Message, TimeSpan.Zero));
                }
            }
        }
        catch (OperationCanceledException)
        {
            LogOutput?.Invoke("Subtitle extraction cancelled by user");
        }
        finally
        {
            summary.TotalTime = DateTime.Now - startTime;
            CurrentFile = null;
            IsProcessing = false;
            IsProcessingChanged?.Invoke(false);
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

            ProcessingCompleted?.Invoke(summary);
        }
    }

    /// <summary>
    /// Pauses processing
    /// </summary>
    public void Pause()
    {
        if (IsProcessing && !IsPaused)
        {
            IsPaused = true;
            LogOutput?.Invoke("Processing paused");
        }
    }

    /// <summary>
    /// Resumes processing
    /// </summary>
    public void Resume()
    {
        if (IsProcessing && IsPaused)
        {
            IsPaused = false;
            LogOutput?.Invoke("Processing resumed");
        }
    }

    /// <summary>
    /// Cancels processing
    /// </summary>
    public void Cancel()
    {
        _cancellationTokenSource?.Cancel();
        IsPaused = false;
        LogOutput?.Invoke("Cancelling processing...");
    }

    /// <summary>
    /// Gets the count of selected pending files
    /// </summary>
    public int GetSelectedPendingCount()
    {
        return Files.Count(f => f.IsSelected && f.Status == FileStatus.Pending);
    }

    /// <summary>
    /// Gets the total size of selected pending files
    /// </summary>
    public long GetSelectedPendingSize()
    {
        return Files.Where(f => f.IsSelected && f.Status == FileStatus.Pending).Sum(f => f.FileSize);
    }
}

/// <summary>
/// Summary of processing results
/// </summary>
public class ProcessingSummary
{
    public int TotalFiles { get; set; }
    public int SuccessfulFiles { get; set; }
    public int FailedFiles { get; set; }
    public int SkippedFiles { get; set; }
    public TimeSpan TotalTime { get; set; }
}
