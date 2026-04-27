using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace MkvProcessor.Services.Subtitle;

/// <summary>
/// A single parsed SRT cue with start/end timestamps and text content.
/// </summary>
public sealed record SrtCue(TimeSpan Start, TimeSpan End, string Text)
{
    public TimeSpan Duration => End - Start;
}

/// <summary>
/// Structured result of validating an SRT file. Contains everything the scorer needs to
/// evaluate quality. A report with <see cref="IsParseable"/> = false is an automatic zero.
/// </summary>
public sealed record SubtitleValidationReport(
    bool FileExists,
    long FileSizeBytes,
    bool IsParseable,
    int CueCount,
    IReadOnlyList<SrtCue> Cues,
    IReadOnlyList<string> Issues)
{
    public static SubtitleValidationReport Missing(string reason) =>
        new(false, 0, false, 0, Array.Empty<SrtCue>(), new[] { reason });
}

/// <summary>
/// Parses SRT files and reports structural problems (zero cues, unparseable timestamps,
/// overlapping or out-of-order cues). Pure logic — no external tools, testable in isolation.
/// </summary>
public static partial class SubtitleValidator
{
    [GeneratedRegex(@"^\s*(\d{1,2}):(\d{2}):(\d{2})[,\.](\d{1,3})\s*-->\s*(\d{1,2}):(\d{2}):(\d{2})[,\.](\d{1,3})",
        RegexOptions.Compiled)]
    private static partial Regex TimingLineRegex();

    /// <summary>
    /// Validates an SRT file on disk. Safe to call on non-existent or empty paths — returns
    /// a report with <see cref="SubtitleValidationReport.IsParseable"/> = false.
    /// </summary>
    public static SubtitleValidationReport Validate(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return SubtitleValidationReport.Missing("No file path provided");

        if (!File.Exists(filePath))
            return SubtitleValidationReport.Missing("File does not exist");

        var info = new FileInfo(filePath);
        if (info.Length == 0)
            return new SubtitleValidationReport(true, 0, false, 0, Array.Empty<SrtCue>(), new[] { "File is empty" });

        string content;
        try
        {
            content = File.ReadAllText(filePath);
        }
        catch (Exception ex)
        {
            return new SubtitleValidationReport(true, info.Length, false, 0, Array.Empty<SrtCue>(),
                new[] { $"Read failed: {ex.Message}" });
        }

        var cues = ParseSrt(content, out var parseIssues);
        var issues = new List<string>(parseIssues);

        if (cues.Count == 0)
        {
            issues.Add("No cues found");
            return new SubtitleValidationReport(true, info.Length, false, 0, cues, issues);
        }

        // Structural checks
        for (int i = 0; i < cues.Count; i++)
        {
            var cue = cues[i];
            if (cue.End <= cue.Start)
                issues.Add($"Cue {i + 1}: end time not after start");

            if (i > 0 && cue.Start < cues[i - 1].Start)
                issues.Add($"Cue {i + 1}: out of order");

            if (i > 0 && cue.Start < cues[i - 1].End)
                issues.Add($"Cue {i + 1}: overlaps previous");
        }

        return new SubtitleValidationReport(true, info.Length, true, cues.Count, cues, issues);
    }

    /// <summary>
    /// Parses SRT content into cues. Tolerant of BOM, CRLF/LF, blank-line variance, and
    /// missing/extra index lines. Unparseable blocks are skipped and reported as issues.
    /// </summary>
    private static List<SrtCue> ParseSrt(string content, out List<string> issues)
    {
        issues = new List<string>();
        var cues = new List<SrtCue>();

        // Normalize line endings and strip BOM
        content = content.Replace("\r\n", "\n").Replace("\r", "\n").TrimStart('\uFEFF');

        // Split into blocks on blank lines
        var blocks = content.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawBlock in blocks)
        {
            var block = rawBlock.Trim('\n', ' ', '\t');
            if (block.Length == 0)
                continue;

            var lines = block.Split('\n');

            // Locate the timing line (may be on line 0 or 1 depending on whether index is present)
            int timingLineIdx = -1;
            Match? timingMatch = null;
            for (int i = 0; i < Math.Min(2, lines.Length); i++)
            {
                var m = TimingLineRegex().Match(lines[i]);
                if (m.Success)
                {
                    timingLineIdx = i;
                    timingMatch = m;
                    break;
                }
            }

            if (timingMatch is null || timingLineIdx < 0)
            {
                issues.Add("Block without valid timing line skipped");
                continue;
            }

            var start = ParseTimestamp(timingMatch, 1);
            var end = ParseTimestamp(timingMatch, 5);

            var textLines = lines.Skip(timingLineIdx + 1).ToArray();
            var text = string.Join('\n', textLines).Trim();

            cues.Add(new SrtCue(start, end, text));
        }

        return cues;
    }

    private static TimeSpan ParseTimestamp(Match m, int groupStart)
    {
        var h = int.Parse(m.Groups[groupStart].Value, CultureInfo.InvariantCulture);
        var mi = int.Parse(m.Groups[groupStart + 1].Value, CultureInfo.InvariantCulture);
        var s = int.Parse(m.Groups[groupStart + 2].Value, CultureInfo.InvariantCulture);
        var ms = int.Parse(m.Groups[groupStart + 3].Value.PadRight(3, '0')[..3], CultureInfo.InvariantCulture);
        return new TimeSpan(0, h, mi, s, ms);
    }
}
