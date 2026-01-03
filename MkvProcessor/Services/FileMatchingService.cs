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

    #region Name Matching

    /// <summary>Pattern for common video quality/codec tags to remove</summary>
    [GeneratedRegex(@"\b(720p|1080p|2160p|4k|uhd|hdr|bluray|blu-ray|bdrip|brrip|webrip|web-dl|webdl|hdtv|dvdrip|x264|x265|h264|h265|hevc|aac|ac3|dts|atmos|proper|repack|internal|extended|unrated|directors.?cut)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex QualityTagsPattern();

    /// <summary>Pattern for release group tags (usually at end in brackets) - but NOT episode parts like (1), (2), (Part 1)</summary>
    [GeneratedRegex(@"[\[\(](?!\d\)|\d\s*\)|part\s*\d)[^\[\]\(\)]{2,}[\]\)]", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex ReleaseGroupPattern();

    /// <summary>Pattern for word separators (dots, dashes, underscores, spaces)</summary>
    [GeneratedRegex(@"[\.\-_\s]+", RegexOptions.Compiled)]
    private static partial Regex WordSeparatorPattern();

    /// <summary>Pattern for ellipsis (multiple dots)</summary>
    [GeneratedRegex(@"\.{2,}", RegexOptions.Compiled)]
    private static partial Regex EllipsisPattern();

    /// <summary>
    /// Normalizes a filename for matching by removing quality tags, release groups, and separators
    /// </summary>
    public static string NormalizeForMatching(string fileName)
    {
        // Remove file extension
        var name = Path.GetFileNameWithoutExtension(fileName);

        // Remove release group tags [Group] or (Group) but keep episode parts like (1)
        name = ReleaseGroupPattern().Replace(name, " ");

        // Remove quality/codec tags
        name = QualityTagsPattern().Replace(name, " ");

        // Remove episode patterns (S01E01, 1x01, etc.) - we want just the title portion
        name = ScenePattern().Replace(name, " ");
        name = StandardPattern().Replace(name, " ");

        // Normalize text consistently
        return NormalizeText(name);
    }

    /// <summary>
    /// Normalizes text for comparison by handling separators, ellipsis, and whitespace
    /// </summary>
    public static string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Replace ellipsis with single space
        var normalized = EllipsisPattern().Replace(text, " ");

        // Replace separators with spaces
        normalized = WordSeparatorPattern().Replace(normalized, " ");

        // Normalize whitespace and lowercase
        return Regex.Replace(normalized, @"\s+", " ").Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Extracts words from a string for token matching
    /// </summary>
    private static HashSet<string> ExtractWords(string text)
    {
        var normalized = NormalizeText(text);
        var words = normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 1) // Skip single characters
            .ToHashSet();
        return words;
    }

    /// <summary>
    /// Tries to match a filename to an episode using name-based matching.
    /// Returns the best matching episode with confidence level, or null if no good match.
    /// Uses a two-pass approach: fast methods first, then expensive fuzzy matching only if needed.
    /// </summary>
    public (Episode? Episode, MatchConfidence Confidence) MatchByName(string fileName, IEnumerable<Episode> episodes)
    {
        var normalizedFileName = NormalizeForMatching(fileName);
        var fileWords = ExtractWords(normalizedFileName);

        if (string.IsNullOrWhiteSpace(normalizedFileName) || fileWords.Count == 0)
            return (null, MatchConfidence.None);

        var episodeList = episodes.ToList();
        Episode? bestMatch = null;
        MatchConfidence bestConfidence = MatchConfidence.None;
        double bestScore = 0;

        // Pass 1: Fast matching (exact contains and word overlap)
        foreach (var episode in episodeList)
        {
            // Normalize episode name the same way as filename for fair comparison
            var normalizedEpisodeName = NormalizeText(episode.Name);

            if (string.IsNullOrWhiteSpace(normalizedEpisodeName))
                continue;

            // Strategy 1: Exact contains (episode name appears in filename)
            // Only match if episode name is meaningful (3+ chars to avoid false positives)
            if (normalizedEpisodeName.Length >= 3 && normalizedFileName.Contains(normalizedEpisodeName))
            {
                // Longer episode names get higher confidence - return immediately for long matches
                var score = (double)normalizedEpisodeName.Length / normalizedFileName.Length;
                if (normalizedEpisodeName.Length >= 8)
                {
                    // Long episode name found - this is almost certainly correct
                    return (episode, MatchConfidence.High);
                }

                if (score > bestScore || bestConfidence < MatchConfidence.High)
                {
                    bestMatch = episode;
                    bestConfidence = MatchConfidence.High;
                    bestScore = score;
                }
                continue;
            }

            // Strategy 2: Word overlap matching
            var episodeWords = ExtractWords(episode.Name);
            if (episodeWords.Count == 0)
                continue;

            var commonWords = fileWords.Intersect(episodeWords).Count();
            var wordOverlap = (double)commonWords / episodeWords.Count;

            if (wordOverlap >= 0.7 && commonWords >= 2)
            {
                if (bestConfidence < MatchConfidence.Medium ||
                    (bestConfidence == MatchConfidence.Medium && wordOverlap > bestScore))
                {
                    bestMatch = episode;
                    bestConfidence = MatchConfidence.Medium;
                    bestScore = wordOverlap;
                }
            }
        }

        // If we found a good match in pass 1, return it (skip expensive fuzzy matching)
        if (bestConfidence >= MatchConfidence.Medium)
            return (bestMatch, bestConfidence);

        // Pass 2: Expensive fuzzy matching - only if no good match found
        // Limit to reasonable filename lengths to avoid slow comparisons
        if (normalizedFileName.Length > 100)
            return (bestMatch, bestConfidence);

        foreach (var episode in episodeList)
        {
            var normalizedEpisodeName = NormalizeText(episode.Name);

            if (string.IsNullOrWhiteSpace(normalizedEpisodeName) || normalizedEpisodeName.Length < 3)
                continue;

            var similarity = CalculateSimilarity(normalizedFileName, normalizedEpisodeName);
            if (similarity >= 0.85)
            {
                if (bestConfidence < MatchConfidence.Low ||
                    (bestConfidence == MatchConfidence.Low && similarity > bestScore))
                {
                    bestMatch = episode;
                    bestConfidence = MatchConfidence.Low;
                    bestScore = similarity;
                }
            }
        }

        return (bestMatch, bestConfidence);
    }

    /// <summary>
    /// Calculates similarity between two strings using Levenshtein distance.
    /// Returns a value between 0 (completely different) and 1 (identical).
    /// </summary>
    private static double CalculateSimilarity(string source, string target)
    {
        if (string.IsNullOrEmpty(source) && string.IsNullOrEmpty(target))
            return 1.0;
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target))
            return 0.0;

        var distance = LevenshteinDistance(source, target);
        var maxLength = Math.Max(source.Length, target.Length);
        return 1.0 - ((double)distance / maxLength);
    }

    /// <summary>
    /// Calculates the Levenshtein distance between two strings.
    /// </summary>
    private static int LevenshteinDistance(string source, string target)
    {
        var sourceLength = source.Length;
        var targetLength = target.Length;

        // Quick exits
        if (sourceLength == 0) return targetLength;
        if (targetLength == 0) return sourceLength;

        // Use two rows instead of full matrix for memory efficiency
        var previousRow = new int[targetLength + 1];
        var currentRow = new int[targetLength + 1];

        // Initialize first row
        for (var j = 0; j <= targetLength; j++)
            previousRow[j] = j;

        for (var i = 1; i <= sourceLength; i++)
        {
            currentRow[0] = i;

            for (var j = 1; j <= targetLength; j++)
            {
                var cost = source[i - 1] == target[j - 1] ? 0 : 1;
                currentRow[j] = Math.Min(
                    Math.Min(currentRow[j - 1] + 1, previousRow[j] + 1),
                    previousRow[j - 1] + cost);
            }

            // Swap rows
            (previousRow, currentRow) = (currentRow, previousRow);
        }

        return previousRow[targetLength];
    }

    #endregion
}
