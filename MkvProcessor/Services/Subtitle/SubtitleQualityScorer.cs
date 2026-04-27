using System.Text.RegularExpressions;

namespace MkvProcessor.Services.Subtitle;

/// <summary>
/// Context a scorer may optionally use to refine scoring — typically the expected language
/// and the parent video's duration (for cue-density checks). All fields are optional; missing
/// context results in neutral scoring for that signal rather than a penalty.
/// </summary>
public sealed record SubtitleScoringContext(
    string? ExpectedLanguage = null,
    TimeSpan? VideoDuration = null,
    SubtitleCodecClass SourceClass = SubtitleCodecClass.Unknown);

/// <summary>
/// Scores a validated SRT on a 0–100 scale. Weights (must sum to 100):
/// <list type="bullet">
///   <item>Cue density vs duration: 25</item>
///   <item>Average cue duration sane: 15</item>
///   <item>No timing issues: 15</item>
///   <item>Low OCR-artifact ratio: 20</item>
///   <item>Language match: 15</item>
///   <item>Source tier bonus: 10</item>
/// </list>
/// A validation report with <c>IsParseable == false</c> short-circuits to 0.
/// </summary>
public static partial class SubtitleQualityScorer
{
    [GeneratedRegex(@"[|~\\\^`]", RegexOptions.Compiled)]
    private static partial Regex ArtifactCharsRegex();

    [GeneratedRegex(@"\brn\b", RegexOptions.Compiled)]
    private static partial Regex RnAsMRegex();

    [GeneratedRegex(@"[A-Za-z]", RegexOptions.Compiled)]
    private static partial Regex AnyLetterRegex();

    /// <summary>
    /// Maps a validation report into a 0–100 quality score given optional context about the
    /// expected language and source video duration.
    /// </summary>
    public static int Score(SubtitleValidationReport report, SubtitleScoringContext context)
    {
        if (!report.IsParseable || report.CueCount == 0)
            return 0;

        double score = 0;
        score += ScoreCueDensity(report, context);   // up to 25
        score += ScoreAverageDuration(report);        // up to 15
        score += ScoreTimingIntegrity(report);        // up to 15
        score += ScoreOcrArtifacts(report);           // up to 20
        score += ScoreLanguageMatch(report, context); // up to 15
        score += ScoreSourceTier(context);            // up to 10

        return (int)Math.Round(Math.Clamp(score, 0, 100));
    }

    private static double ScoreCueDensity(SubtitleValidationReport report, SubtitleScoringContext ctx)
    {
        // Without a known video duration, the cue density signal is neutral (full credit).
        if (ctx.VideoDuration is null || ctx.VideoDuration.Value.TotalMinutes < 0.5)
            return 25;

        var cuesPerMinute = report.CueCount / ctx.VideoDuration.Value.TotalMinutes;

        // Sweet spot: 3–25 cues per minute. Outside range, fall off linearly.
        if (cuesPerMinute >= 3 && cuesPerMinute <= 25)
            return 25;
        if (cuesPerMinute is >= 1 and < 3)
            return 25 * (cuesPerMinute - 1) / 2; // 0 at 1/min, 25 at 3/min
        if (cuesPerMinute is > 25 and <= 60)
            return 25 * (60 - cuesPerMinute) / 35;
        return 0;
    }

    private static double ScoreAverageDuration(SubtitleValidationReport report)
    {
        if (report.Cues.Count == 0) return 0;

        var avg = report.Cues.Average(c => c.Duration.TotalSeconds);

        // Healthy subtitles average between ~0.4s (fast flash) and 7s (long held line).
        if (avg is >= 0.4 and <= 7.0)
            return 15;
        if (avg is > 0 and < 0.4)
            return 15 * (avg / 0.4);
        if (avg is > 7.0 and <= 15.0)
            return 15 * (15.0 - avg) / 8.0;
        return 0;
    }

    private static double ScoreTimingIntegrity(SubtitleValidationReport report)
    {
        // Count only timing-related issues (reported as "overlaps", "out of order", "end time not after start")
        var timingIssues = report.Issues.Count(i =>
            i.Contains("overlaps", StringComparison.OrdinalIgnoreCase) ||
            i.Contains("out of order", StringComparison.OrdinalIgnoreCase) ||
            i.Contains("end time", StringComparison.OrdinalIgnoreCase));

        if (timingIssues == 0) return 15;
        if (timingIssues <= 2) return 10;
        if (timingIssues <= 5) return 5;
        return 0;
    }

    private static double ScoreOcrArtifacts(SubtitleValidationReport report)
    {
        if (report.Cues.Count == 0) return 0;

        long totalLetters = 0;
        long artifactCount = 0;
        long rnCount = 0;

        foreach (var cue in report.Cues)
        {
            var text = cue.Text;
            artifactCount += ArtifactCharsRegex().Matches(text).Count;
            rnCount += RnAsMRegex().Matches(text).Count;
            totalLetters += AnyLetterRegex().Matches(text).Count;
        }

        if (totalLetters == 0) return 0;

        var artifactRatio = (double)(artifactCount + rnCount * 2) / totalLetters;

        // 0% artifacts → 20; 5% → 10; 10%+ → 0
        if (artifactRatio <= 0) return 20;
        if (artifactRatio >= 0.10) return 0;
        return 20 * (1 - artifactRatio / 0.10);
    }

    /// <summary>
    /// Very lightweight language check: counts occurrences of top stopwords for the expected
    /// language. Not a real language detector — just enough to spot catastrophic mismatches
    /// (e.g., OCR produced French text when we asked for English).
    /// </summary>
    private static double ScoreLanguageMatch(SubtitleValidationReport report, SubtitleScoringContext ctx)
    {
        if (string.IsNullOrWhiteSpace(ctx.ExpectedLanguage) || ctx.ExpectedLanguage == "und")
            return 15;

        var stopwords = GetStopwords(ctx.ExpectedLanguage);
        if (stopwords.Length == 0)
            return 15;

        var text = string.Join(' ', report.Cues.Select(c => c.Text)).ToLowerInvariant();
        if (text.Length < 50)
            return 10; // too little text to judge confidently

        int hits = 0;
        foreach (var word in stopwords)
        {
            if (Regex.IsMatch(text, $@"\b{Regex.Escape(word)}\b"))
                hits++;
        }

        // 6+ stopword hits = full credit; linear below
        if (hits >= 6) return 15;
        return 15 * (hits / 6.0);
    }

    private static double ScoreSourceTier(SubtitleScoringContext ctx)
    {
        // Prefer genuine text tracks over OCR output — a small tiebreaker bonus.
        return ctx.SourceClass switch
        {
            SubtitleCodecClass.Text => 10,
            SubtitleCodecClass.Teletext => 7,
            SubtitleCodecClass.PgsBitmap => 5,
            SubtitleCodecClass.VobSubBitmap => 5,
            SubtitleCodecClass.DvbBitmap => 4,
            _ => 3
        };
    }

    private static string[] GetStopwords(string language) => language.ToLowerInvariant() switch
    {
        "eng" or "en" => new[] { "the", "and", "you", "that", "what", "with", "this", "have", "are", "for" },
        "spa" or "es" => new[] { "que", "los", "las", "por", "con", "una", "del", "para", "esto", "está" },
        "fra" or "fr" => new[] { "que", "les", "des", "est", "pas", "pour", "dans", "une", "vous", "nous" },
        "deu" or "de" => new[] { "der", "die", "das", "und", "ich", "nicht", "ist", "mit", "für", "sie" },
        "ita" or "it" => new[] { "che", "non", "una", "per", "con", "questo", "come", "sono", "della", "gli" },
        "por" or "pt" => new[] { "que", "não", "para", "uma", "com", "por", "mais", "está", "eles", "como" },
        "nld" or "nl" => new[] { "het", "een", "van", "dat", "niet", "met", "voor", "maar", "zijn", "heb" },
        _ => Array.Empty<string>()
    };
}
