using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MkvProcessor.Models;

/// <summary>
/// Represents a file matched to an episode for renaming
/// </summary>
public partial class FileMatch : ObservableObject
{
    /// <summary>Original file path</summary>
    [ObservableProperty]
    private string _originalFilePath = string.Empty;

    /// <summary>Original file name (without path)</summary>
    [ObservableProperty]
    private string _originalFileName = string.Empty;

    /// <summary>New file name (preview)</summary>
    [ObservableProperty]
    private string _newFileName = string.Empty;

    /// <summary>Matched episode (null if no match)</summary>
    [ObservableProperty]
    private Episode? _matchedEpisode;

    /// <summary>Detected season number from filename</summary>
    [ObservableProperty]
    private int _detectedSeasonNumber;

    /// <summary>Detected episode number from filename</summary>
    [ObservableProperty]
    private int _detectedEpisodeNumber;

    /// <summary>Match confidence level</summary>
    [ObservableProperty]
    private MatchConfidence _confidence = MatchConfidence.None;

    /// <summary>Whether this file is selected for renaming</summary>
    [ObservableProperty]
    private bool _isSelected = true;

    /// <summary>Whether rename was successful (null = not yet attempted)</summary>
    [ObservableProperty]
    private bool? _renameSuccess;

    /// <summary>Error message if rename failed</summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>File extension (preserved during rename)</summary>
    public string Extension => Path.GetExtension(OriginalFilePath);

    /// <summary>Directory containing the file</summary>
    public string Directory => Path.GetDirectoryName(OriginalFilePath) ?? string.Empty;

    /// <summary>Full new path after rename</summary>
    public string NewFilePath => string.IsNullOrEmpty(NewFileName)
        ? string.Empty
        : Path.Combine(Directory, NewFileName);
}
