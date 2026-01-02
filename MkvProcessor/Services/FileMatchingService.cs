using System.IO;
using System.Text.RegularExpressions;
using MkvProcessor.Models;

namespace MkvProcessor.Services;

/// <summary>
/// Service for matching video files to episode information using pattern detection
/// </summary>
public partial class FileMatchingService
{
    /// <summary>Pattern: S01E01 or S1E1 (Scene format - highest confidence)</summary>
    [GeneratedRegex(@"[Ss](\d{1,2})[Ee](\d{1,3})", RegexOptions.Compiled)]
    private static partial Regex ScenePattern();

    /// <summary>Pattern: 1x01 or 01x01 (Standard format - medium confidence)</summary>
    [GeneratedRegex(@"(\d{1,2})x(\d{2,3})", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex StandardPattern();

    /// <summary>Pattern: 101, 201, etc. (Compact format - low confidence, single digit season only)</summary>
    [GeneratedRegex(@"(?<![0-9])(\d)(\d{2})(?![0-9])", RegexOptions.Compiled)]
    private static partial Regex CompactPattern();

    /// <summary>Pattern for illegal filename characters</summary>
    [GeneratedRegex(@"[<>:""/\\|?*]", RegexOptions.Compiled)]
    private static partial Regex IllegalCharsPattern();

    /// <summary>
    /// Extracts season and episode numbers from a filename
    /// </summary>
    /// <returns>Tuple of (Season, Episode, Confidence)</returns>
    public (int Season, int Episode, MatchConfidence Confidence) DetectEpisode(string fileName)
    {
        // Try Scene format first (S01E01) - highest confidence
        var sceneMatch = ScenePattern().Match(fileName);
        if (sceneMatch.Success)
        {
            return (
                int.Parse(sceneMatch.Groups[1].Value),
                int.Parse(sceneMatch.Groups[2].Value),
                MatchConfidence.High
            );
        }

        // Try Standard format (1x01) - medium confidence
        var standardMatch = StandardPattern().Match(fileName);
        if (standardMatch.Success)
        {
            return (
                int.Parse(standardMatch.Groups[1].Value),
                int.Parse(standardMatch.Groups[2].Value),
                MatchConfidence.Medium
            );
        }

        // Try Compact format (101) - low confidence
        var compactMatch = CompactPattern().Match(fileName);
        if (compactMatch.Success)
        {
            return (
                int.Parse(compactMatch.Groups[1].Value),
                int.Parse(compactMatch.Groups[2].Value),
                MatchConfidence.Low
            );
        }

        return (0, 0, MatchConfidence.None);
    }

    /// <summary>
    /// Matches files to episodes from a show
    /// </summary>
    public List<FileMatch> MatchFiles(IEnumerable<string> filePaths, TvShow show, NamingFormat format)
    {
        var matches = new List<FileMatch>();

        foreach (var filePath in filePaths)
        {
            var fileName = Path.GetFileName(filePath);
            var (season, episode, confidence) = DetectEpisode(fileName);

            var match = new FileMatch
            {
                OriginalFilePath = filePath,
                OriginalFileName = fileName,
                DetectedSeasonNumber = season,
                DetectedEpisodeNumber = episode,
                Confidence = confidence
            };

            // Try to find matching episode
            if (confidence != MatchConfidence.None)
            {
                var seasonData = show.Seasons.FirstOrDefault(s => s.Number == season);
                var episodeData = seasonData?.Episodes.FirstOrDefault(e => e.EpisodeNumber == episode);

                if (episodeData != null)
                {
                    match.MatchedEpisode = episodeData;
                    match.NewFileName = GenerateFileName(episodeData, show.Name, format, Path.GetExtension(filePath));
                }
                else
                {
                    // Pattern found but no matching episode in show data
                    match.Confidence = MatchConfidence.None;
                }
            }

            matches.Add(match);
        }

        return matches;
    }

    /// <summary>
    /// Generates new filename based on episode data and format
    /// </summary>
    public string GenerateFileName(Episode episode, string showName, NamingFormat format, string extension)
    {
        var episodeCode = format switch
        {
            NamingFormat.Standard => $"{episode.SeasonNumber:D2}x{episode.EpisodeNumber:D2}",
            NamingFormat.Scene => $"S{episode.SeasonNumber:D2}E{episode.EpisodeNumber:D2}",
            _ => $"{episode.SeasonNumber:D2}x{episode.EpisodeNumber:D2}"
        };

        var fileName = $"{showName} - {episodeCode} - {episode.Name}{extension}";
        return SanitizeFileName(fileName);
    }

    /// <summary>
    /// Updates match preview names when format or show changes
    /// </summary>
    public void UpdatePreviewNames(IEnumerable<FileMatch> matches, string showName, NamingFormat format)
    {
        foreach (var match in matches)
        {
            if (match.MatchedEpisode != null)
            {
                match.NewFileName = GenerateFileName(
                    match.MatchedEpisode,
                    showName,
                    format,
                    match.Extension);
            }
        }
    }

    /// <summary>
    /// Rematches files against a different show or after show data refresh
    /// </summary>
    public void RematchFiles(IEnumerable<FileMatch> matches, TvShow show, NamingFormat format)
    {
        foreach (var match in matches)
        {
            // Re-detect from original filename
            var (season, episode, confidence) = DetectEpisode(match.OriginalFileName);

            match.DetectedSeasonNumber = season;
            match.DetectedEpisodeNumber = episode;
            match.Confidence = confidence;
            match.MatchedEpisode = null;
            match.NewFileName = string.Empty;

            if (confidence != MatchConfidence.None)
            {
                var seasonData = show.Seasons.FirstOrDefault(s => s.Number == season);
                var episodeData = seasonData?.Episodes.FirstOrDefault(e => e.EpisodeNumber == episode);

                if (episodeData != null)
                {
                    match.MatchedEpisode = episodeData;
                    match.NewFileName = GenerateFileName(episodeData, show.Name, format, match.Extension);
                }
                else
                {
                    match.Confidence = MatchConfidence.None;
                }
            }
        }
    }

    /// <summary>
    /// Sanitizes filename by removing illegal characters
    /// </summary>
    public static string SanitizeFileName(string fileName)
    {
        // Remove illegal characters
        var sanitized = IllegalCharsPattern().Replace(fileName, "");

        // Replace multiple spaces with single space
        sanitized = Regex.Replace(sanitized, @"\s+", " ");

        // Trim whitespace
        sanitized = sanitized.Trim();

        return sanitized;
    }

    /// <summary>
    /// Gets supported video file extensions
    /// </summary>
    public static string[] SupportedExtensions => [".mkv", ".mp4", ".avi", ".m4v", ".mov", ".wmv", ".ts"];

    /// <summary>
    /// Checks if a file is a supported video file
    /// </summary>
    public static bool IsSupportedVideoFile(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return SupportedExtensions.Contains(extension);
    }

    /// <summary>
    /// Gets video files from a directory
    /// </summary>
    public static IEnumerable<string> GetVideoFilesFromDirectory(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
            return [];

        return Directory.EnumerateFiles(directoryPath)
            .Where(IsSupportedVideoFile)
            .OrderBy(f => f);
    }
}
