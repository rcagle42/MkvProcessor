using System.IO;
using System.Text.RegularExpressions;
using Nikse.SubtitleEdit.Core.Dictionaries;

namespace MkvProcessor.Services.Subtitle;

/// <summary>
/// OCR-aware word corrector tailored to the errors Tesseract tends to produce on subtitle
/// bitmaps. Operates in three layers of increasing looseness:
/// <list type="number">
///   <item>
///     Direct replacement from Subtitle Edit's curated <c>WordReplaceList</c> (2,798 hand-
///     maintained entries like <c>aimost→almost</c>, <c>guud→good</c>).
///   </item>
///   <item>
///     Dictionary-membership check against a bundled top-10k English frequency list. Words
///     that are already real don't get touched — this is the guardrail that keeps proper
///     nouns and correct words intact.
///   </item>
///   <item>
///     Single-substitution variant generation using a fixed OCR confusion table
///     (<c>i↔l</c>, <c>0↔O</c>, <c>rn↔m</c>, <c>cl↔d</c>, leading-<c>l</c> restoration,
///     <c>K→It</c>, <c>L→it</c>). First variant that lands in the dictionary wins. This
///     catches errors SE's list doesn't know about.
///   </item>
/// </list>
/// Applied per-cue in VobSubEmbeddedOcrStrategy. Zero-overhead when no correction is needed
/// (dictionary membership is a HashSet lookup, O(1) per word).
/// </summary>
public partial class SubtitleWordCorrector
{
    private readonly HashSet<string> _knownWords;
    private readonly IReadOnlyDictionary<string, string> _seReplaceList;

    /// <summary>
    /// OCR character-confusion table, applied one substitution at a time. Each tuple is
    /// (needle, replacement); both directions are included where appropriate. Multi-char
    /// substitutions (<c>rn↔m</c>, <c>cl↔d</c>) handle ligature-style misreads.
    /// </summary>
    private static readonly (string from, string to)[] OcrSubstitutions = new[]
    {
        ("i", "l"), ("l", "i"),
        ("I", "l"), ("l", "I"),
        ("1", "l"), ("l", "1"), ("1", "I"), ("I", "1"),
        ("0", "o"), ("o", "0"), ("0", "O"), ("O", "0"),
        ("rn", "m"), ("m", "rn"),
        ("cl", "d"), ("d", "cl"),
        ("nn", "m"),
        ("vv", "w"), ("w", "vv"),
        ("ii", "u"), ("u", "ii"),
    };

    /// <summary>
    /// Letters to try prepending to short un-matched words. Thin leading strokes (l, I, i)
    /// are the most common silent drops in subtitle OCR.
    /// </summary>
    private static readonly string[] LeadingRestorePrefixes = { "l", "I", "i" };

    /// <summary>
    /// Whole-word substitutions specific to visual errors that the character-substitution
    /// rules can't express. Case-sensitive lookup — these are exact spellings we've seen
    /// Tesseract produce when it should have read something else.
    /// </summary>
    private static readonly Dictionary<string, string> SpecificWholeWordFixes = new()
    {
        { "K's",  "It's" },
        { "iL",   "it" },
        { "iL.",  "it." },
        { "L.",   "it." },
    };

    [GeneratedRegex(@"[A-Za-z'][A-Za-z0-9']*")]
    private static partial Regex WordRegex();

    public SubtitleWordCorrector(string wordListPath, OcrFixReplaceList? seList)
    {
        _knownWords = LoadWordList(wordListPath);
        _seReplaceList = seList?.WordReplaceList ?? new Dictionary<string, string>();
    }

    /// <summary>
    /// Corrects every word-like token in the input while preserving punctuation and
    /// whitespace exactly. Non-word spans pass through untouched.
    /// </summary>
    public string CorrectText(string text)
    {
        if (string.IsNullOrEmpty(text) || _knownWords.Count == 0)
            return text;

        return WordRegex().Replace(text, match => CorrectWord(match.Value));
    }

    private string CorrectWord(string word)
    {
        // Skip very short tokens — single letters are too ambiguous to correct safely
        // ("I" and "a" are both legitimate and must not be touched).
        if (word.Length < 2) return word;

        // 1. Specific whole-word fixes we've observed but SE's list doesn't cover.
        if (SpecificWholeWordFixes.TryGetValue(word, out var specific))
            return specific;

        // 2. SE's curated word-replace list — case-sensitive exact match.
        if (_seReplaceList.TryGetValue(word, out var seFix))
            return seFix;

        // 3. Already a known real word? Leave it alone. This is what protects proper nouns
        //    and dictionary words from being "corrected" to something else.
        if (IsKnownWord(word))
            return word;

        // 4. Try single-substitution OCR variants and return the first one that's a real
        //    word. Order matters: earlier substitutions in the table win ties.
        foreach (var variant in GenerateVariants(word))
        {
            if (IsKnownWord(variant))
                return variant;
        }

        // 5. Give up — the word isn't fixable by our rules. Leaving it unchanged is safer
        //    than guessing.
        return word;
    }

    private bool IsKnownWord(string word)
    {
        // The bundled list is lowercase. Strip trailing apostrophe-s ("dog's" → "dog") so
        // possessives of real words also pass the membership check.
        var lower = word.ToLowerInvariant();
        if (_knownWords.Contains(lower)) return true;
        if (lower.EndsWith("'s") && _knownWords.Contains(lower[..^2])) return true;
        return false;
    }

    /// <summary>
    /// Generates candidate corrections for a word by applying a single substitution from
    /// the OCR confusion table at each valid position, plus leading-letter restorations.
    /// Variant generation is eager but bounded — a typical 6-letter word produces fewer
    /// than 30 candidates, each a HashSet lookup away from acceptance.
    /// </summary>
    private static IEnumerable<string> GenerateVariants(string word)
    {
        foreach (var (from, to) in OcrSubstitutions)
        {
            int searchStart = 0;
            while (searchStart <= word.Length - from.Length)
            {
                int idx = word.IndexOf(from, searchStart, StringComparison.Ordinal);
                if (idx < 0) break;
                yield return word[..idx] + to + word[(idx + from.Length)..];
                searchStart = idx + 1;
            }
        }

        // Leading-letter restoration — thin-stroke letters often get dropped at word start.
        foreach (var prefix in LeadingRestorePrefixes)
            yield return prefix + word;
    }

    private static HashSet<string> LoadWordList(string path)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
            return set;

        try
        {
            foreach (var line in File.ReadLines(path))
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0)
                    set.Add(trimmed);
            }
        }
        catch
        {
            // Graceful degradation — an unreadable word list disables correction rather
            // than failing the whole OCR run.
        }
        return set;
    }
}
