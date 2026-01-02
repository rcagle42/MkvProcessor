using System.IO;
using MkvProcessor.Models;

namespace MkvProcessor.Services;

/// <summary>
/// Service for performing file rename operations
/// </summary>
public class RenamingService
{
    /// <summary>Event raised when log messages are generated</summary>
    public event Action<string>? LogOutput;

    /// <summary>
    /// Renames a single file in place
    /// </summary>
    public RenameResult RenameFile(FileMatch match)
    {
        if (string.IsNullOrEmpty(match.NewFileName))
        {
            return new RenameResult
            {
                OriginalPath = match.OriginalFilePath,
                NewPath = string.Empty,
                Success = false,
                ErrorMessage = "No new filename specified"
            };
        }

        var newPath = Path.Combine(match.Directory, match.NewFileName);

        // Check if source file exists
        if (!File.Exists(match.OriginalFilePath))
        {
            return new RenameResult
            {
                OriginalPath = match.OriginalFilePath,
                NewPath = newPath,
                Success = false,
                ErrorMessage = "Source file not found"
            };
        }

        // Check if target already exists (and isn't the same file with different case)
        if (File.Exists(newPath) &&
            !string.Equals(match.OriginalFilePath, newPath, StringComparison.OrdinalIgnoreCase))
        {
            return new RenameResult
            {
                OriginalPath = match.OriginalFilePath,
                NewPath = newPath,
                Success = false,
                ErrorMessage = "Target file already exists"
            };
        }

        try
        {
            File.Move(match.OriginalFilePath, newPath);

            LogOutput?.Invoke($"Renamed: {match.OriginalFileName} -> {match.NewFileName}");

            return new RenameResult
            {
                OriginalPath = match.OriginalFilePath,
                NewPath = newPath,
                Success = true
            };
        }
        catch (Exception ex)
        {
            LogOutput?.Invoke($"Error renaming {match.OriginalFileName}: {ex.Message}");

            return new RenameResult
            {
                OriginalPath = match.OriginalFilePath,
                NewPath = newPath,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Renames multiple files, returns results
    /// </summary>
    public List<RenameResult> RenameFiles(IEnumerable<FileMatch> matches, IProgress<int>? progress = null)
    {
        var results = new List<RenameResult>();
        var matchList = matches.ToList();
        var total = matchList.Count;

        for (int i = 0; i < total; i++)
        {
            var match = matchList[i];
            var result = RenameFile(match);
            results.Add(result);

            // Update the match object with result
            match.RenameSuccess = result.Success;
            match.ErrorMessage = result.ErrorMessage;

            // Update original path if successful (for potential undo)
            if (result.Success)
            {
                match.OriginalFilePath = result.NewPath;
                match.OriginalFileName = match.NewFileName;
            }

            progress?.Report((i + 1) * 100 / total);
        }

        var successCount = results.Count(r => r.Success);
        LogOutput?.Invoke($"Renamed {successCount}/{total} files");

        return results;
    }

    /// <summary>
    /// Validates all renames can be performed (checks for conflicts)
    /// </summary>
    public List<RenameConflict> ValidateRenames(IEnumerable<FileMatch> matches)
    {
        var conflicts = new List<RenameConflict>();
        var matchList = matches.Where(m => m.IsSelected && !string.IsNullOrEmpty(m.NewFileName)).ToList();
        var targetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var match in matchList)
        {
            var newPath = Path.Combine(match.Directory, match.NewFileName);

            // Check if source exists
            if (!File.Exists(match.OriginalFilePath))
            {
                conflicts.Add(new RenameConflict
                {
                    Match = match,
                    ConflictType = "SourceMissing",
                    Message = $"Source file not found: {match.OriginalFileName}"
                });
                continue;
            }

            // Check if target already exists on disk (excluding same file)
            if (File.Exists(newPath) &&
                !string.Equals(match.OriginalFilePath, newPath, StringComparison.OrdinalIgnoreCase))
            {
                conflicts.Add(new RenameConflict
                {
                    Match = match,
                    ConflictType = "FileExists",
                    Message = $"Target already exists: {match.NewFileName}"
                });
                continue;
            }

            // Check for duplicate targets within the batch
            if (targetPaths.Contains(newPath))
            {
                conflicts.Add(new RenameConflict
                {
                    Match = match,
                    ConflictType = "DuplicateTarget",
                    Message = $"Duplicate target name: {match.NewFileName}"
                });
                continue;
            }

            targetPaths.Add(newPath);
        }

        return conflicts;
    }

    /// <summary>
    /// Undoes a rename operation (swaps original and new paths)
    /// </summary>
    public bool UndoRename(RenameResult result)
    {
        if (!result.Success || string.IsNullOrEmpty(result.NewPath))
            return false;

        try
        {
            if (File.Exists(result.NewPath))
            {
                File.Move(result.NewPath, result.OriginalPath);
                LogOutput?.Invoke($"Undone: {Path.GetFileName(result.NewPath)} -> {Path.GetFileName(result.OriginalPath)}");
                return true;
            }
        }
        catch (Exception ex)
        {
            LogOutput?.Invoke($"Error undoing rename: {ex.Message}");
        }

        return false;
    }
}

/// <summary>
/// Result of a rename operation
/// </summary>
public class RenameResult
{
    /// <summary>Original file path before rename</summary>
    public string OriginalPath { get; init; } = string.Empty;

    /// <summary>New file path after rename</summary>
    public string NewPath { get; init; } = string.Empty;

    /// <summary>Whether the rename was successful</summary>
    public bool Success { get; init; }

    /// <summary>Error message if rename failed</summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Represents a rename conflict detected during validation
/// </summary>
public class RenameConflict
{
    /// <summary>The file match with the conflict</summary>
    public required FileMatch Match { get; init; }

    /// <summary>Type of conflict (FileExists, DuplicateTarget, SourceMissing)</summary>
    public required string ConflictType { get; init; }

    /// <summary>Human-readable conflict description</summary>
    public required string Message { get; init; }
}
