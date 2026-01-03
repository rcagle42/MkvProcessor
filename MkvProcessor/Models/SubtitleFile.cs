using CommunityToolkit.Mvvm.ComponentModel;

namespace MkvProcessor.Models;

/// <summary>
/// Status of subtitle file conversion
/// </summary>
public enum SubtitleConversionStatus
{
    Pending,
    Converting,
    Complete,
    Error,
    Skipped
}

/// <summary>
/// Represents a subtitle file in the conversion queue
/// </summary>
public partial class SubtitleFile : ObservableObject
{
    /// <summary>Full path to the .sup file</summary>
    [ObservableProperty]
    private string _filePath = string.Empty;

    /// <summary>File name without path</summary>
    [ObservableProperty]
    private string _fileName = string.Empty;

    /// <summary>File size in bytes</summary>
    [ObservableProperty]
    private long _fileSize;

    /// <summary>Whether this file is selected for conversion</summary>
    [ObservableProperty]
    private bool _isSelected = true;

    /// <summary>Current conversion status</summary>
    [ObservableProperty]
    private SubtitleConversionStatus _status = SubtitleConversionStatus.Pending;

    /// <summary>Conversion progress (0-100)</summary>
    [ObservableProperty]
    private double _progress;

    /// <summary>Human-readable status text</summary>
    [ObservableProperty]
    private string _statusText = "Pending";

    /// <summary>Output .srt file path after conversion</summary>
    [ObservableProperty]
    private string? _outputPath;

    /// <summary>Error message if conversion failed</summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// Gets the expected output file name (.srt)
    /// </summary>
    public string ExpectedOutputFileName =>
        System.IO.Path.ChangeExtension(FileName, ".srt");

    /// <summary>
    /// Gets the expected output file path (.srt in same directory)
    /// </summary>
    public string ExpectedOutputPath =>
        System.IO.Path.ChangeExtension(FilePath, ".srt");

    /// <summary>
    /// Gets formatted file size for display
    /// </summary>
    public string FileSizeFormatted
    {
        get
        {
            if (FileSize < 1024)
                return $"{FileSize} B";
            if (FileSize < 1024 * 1024)
                return $"{FileSize / 1024.0:F1} KB";
            return $"{FileSize / (1024.0 * 1024.0):F1} MB";
        }
    }
}
