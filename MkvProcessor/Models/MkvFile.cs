using CommunityToolkit.Mvvm.ComponentModel;

namespace MkvProcessor.Models;

/// <summary>
/// Represents a single MKV file in the processing queue
/// </summary>
public partial class MkvFile : ObservableObject
{
    /// <summary>
    /// Full path to the MKV file
    /// </summary>
    [ObservableProperty]
    private string _filePath = string.Empty;

    /// <summary>
    /// File name without path
    /// </summary>
    [ObservableProperty]
    private string _fileName = string.Empty;

    /// <summary>
    /// File size in bytes
    /// </summary>
    [ObservableProperty]
    private long _fileSize;

    /// <summary>
    /// Duration of the video
    /// </summary>
    [ObservableProperty]
    private TimeSpan _duration;

    /// <summary>
    /// Primary audio codec (e.g., "truehd", "dts", "aac")
    /// </summary>
    [ObservableProperty]
    private string _audioCodec = string.Empty;

    /// <summary>
    /// Whether the file has subtitle streams
    /// </summary>
    [ObservableProperty]
    private bool _hasSubtitles;

    /// <summary>
    /// Whether this file is selected for processing
    /// </summary>
    [ObservableProperty]
    private bool _isSelected = true;

    /// <summary>
    /// Current processing status
    /// </summary>
    [ObservableProperty]
    private FileStatus _status = FileStatus.Pending;

    /// <summary>
    /// Processing progress (0-100)
    /// </summary>
    [ObservableProperty]
    private double _progress;

    /// <summary>
    /// Current processing step description
    /// </summary>
    [ObservableProperty]
    private string _currentStep = string.Empty;

    /// <summary>
    /// Error message if processing failed
    /// </summary>
    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>
    /// Path to the output file (set after processing)
    /// </summary>
    [ObservableProperty]
    private string _outputPath = string.Empty;

    /// <summary>
    /// Output folder for this file (default: {SourceDirectory}\Processed)
    /// </summary>
    [ObservableProperty]
    private string _outputFolder = string.Empty;

    /// <summary>
    /// Whether this file needs audio transcoding (TrueHD, DTS, etc.)
    /// </summary>
    public bool NeedsAudioTranscode => AudioCodec.ToLowerInvariant() switch
    {
        "truehd" or "dts" or "dca" or "dts-hd" or "mlp" or "pcm_bluray" or "pcm_dvd" => true,
        _ => false
    };

    /// <summary>
    /// Human-readable file size
    /// </summary>
    public string FileSizeFormatted => FormatFileSize(FileSize);

    /// <summary>
    /// Human-readable duration
    /// </summary>
    public string DurationFormatted => Duration.ToString(@"h\:mm\:ss");

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
}
