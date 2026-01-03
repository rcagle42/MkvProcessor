using System.IO;
using System.Text.RegularExpressions;

namespace MkvProcessor.Services;

/// <summary>
/// Post-processes SRT files to fix common OCR mistakes
/// </summary>
public static partial class SrtPostProcessor
{
    /// <summary>
    /// Processes an SRT file and applies OCR corrections
    /// </summary>
    public static async Task ProcessFileAsync(string srtPath)
    {
        if (!File.Exists(srtPath))
            return;

        var content = await File.ReadAllTextAsync(srtPath);
        var corrected = ApplyCorrections(content);

        if (content != corrected)
        {
            await File.WriteAllTextAsync(srtPath, corrected);
        }
    }

    /// <summary>
    /// Applies all correction rules to the content
    /// </summary>
    public static string ApplyCorrections(string content)
    {
        var result = content;

        // === MUSIC NOTES ===
        // These are very common OCR errors for ♪ symbol

        // Trailing 'f' at end of subtitle lines (very common OCR for ♪)
        result = TrailingF_MusicNote().Replace(result, "♪");

        // Trailing 'I' at end of subtitle lines in music context
        result = TrailingI_MusicNote().Replace(result, "♪");

        // ~ at line start -> ♪
        result = Tilde_MusicNote().Replace(result, "♪");

        // < at line start -> ♪
        result = LessThan_MusicNote().Replace(result, "♪");

        // # at line boundaries
        result = Hash_MusicNote().Replace(result, "♪");

        // * at line boundaries
        result = Star_MusicNote().Replace(result, "♪");

        // J followed by space at line start before capital letter
        result = J_MusicNote().Replace(result, "♪ ");

        // === NUMBER 1 AS LETTER I ===

        // Standalone 1 between words: "If 1 didn't" -> "If I didn't"
        result = Number1_AsI().Replace(result, "${before}I${after}");

        // === SYMBOLS AS LETTERS ===

        // ! as I in words: "do! thrill" -> "do I thrill"
        result = Exclamation_AsI().Replace(result, "${before}I${after}");

        // @ as 'a': "...@ bottle" -> "...a bottle"
        result = AtSign_AsA().Replace(result, "${before}a${after}");

        // === PIPE AND SLASH AS 'I' ===

        // Contractions: |'m, /'ll, |'ve, /'d -> I'm, I'll, I've, I'd
        result = PipeSlashContraction().Replace(result, "I'");

        // Standalone | or / as word (surrounded by spaces/punctuation/line bounds)
        result = PipeSlashStandalone().Replace(result, "${before}I${after}");

        // | or / at word start followed by lowercase letter (It, In, Is, If, etc.)
        result = PipeSlashWordStart().Replace(result, "${before}I${letter}");

        // | or / followed by space (likely meant to be "I ")
        result = PipeSlashBeforeSpace().Replace(result, "${before}I ");

        // === PIPE AND SLASH AS 'l' (lowercase L) ===

        // | or / between lowercase letters: wi||ing -> willing
        result = PipeSlashMidWord().Replace(result, "l");

        // | or / at end of word after lowercase: gir| -> girl
        result = PipeSlashWordEnd().Replace(result, "${letter}l");

        // === LOWERCASE L AS 'I' ===

        // Common words starting with lowercase L that should be I
        result = LowercaseL_Contractions().Replace(result, "${before}I'${suffix}");
        result = LowercaseL_It().Replace(result, "${before}It${suffix}");
        result = LowercaseL_In().Replace(result, "${before}In${suffix}");
        result = LowercaseL_If().Replace(result, "${before}If${suffix}");
        result = LowercaseL_Is().Replace(result, "${before}Is${suffix}");

        // === ELLIPSIS FIXES ===

        // .. followed directly by letter (missing space or period)
        // "..In early" -> "...In early" or ".. In early"
        result = DoubleDotNoSpace().Replace(result, "...$1");

        // === ZERO/O CONFUSION ===

        // 0 at word start followed by lowercase: 0n -> On, 0f -> Of
        result = ZeroAsO().Replace(result, "${before}O${letter}");

        // === CLEANUP ===

        // Multiple spaces to single
        result = DoubleSpace().Replace(result, " ");

        // Trim leading/trailing spaces on lines
        result = LeadingTrailingSpaces().Replace(result, "");

        return result;
    }

    #region Generated Regex Patterns

    // === MUSIC NOTES ===

    // Trailing 'f' at end of lines (common OCR for ♪): "didn't care f" -> "didn't care ♪"
    [GeneratedRegex(@"\s+f\s*$", RegexOptions.Multiline)]
    private static partial Regex TrailingF_MusicNote();

    // Trailing 'I' at end of lines when preceded by ? or other punctuation (music context)
    [GeneratedRegex(@"(?<=[?!.])\s+I\s*$", RegexOptions.Multiline)]
    private static partial Regex TrailingI_MusicNote();

    // ~ at line start
    [GeneratedRegex(@"^\s*~\s*", RegexOptions.Multiline)]
    private static partial Regex Tilde_MusicNote();

    // < at line start
    [GeneratedRegex(@"^\s*<\s*", RegexOptions.Multiline)]
    private static partial Regex LessThan_MusicNote();

    // # at line boundaries
    [GeneratedRegex(@"(?<=^)\s*#\s*|\s*#\s*(?=$)", RegexOptions.Multiline)]
    private static partial Regex Hash_MusicNote();

    // * at line boundaries
    [GeneratedRegex(@"(?<=^)\s*\*\s*|\s*\*\s*(?=$)", RegexOptions.Multiline)]
    private static partial Regex Star_MusicNote();

    // J followed by space at line start before capital letter
    [GeneratedRegex(@"(?<=^)J\s+(?=[A-Z])", RegexOptions.Multiline)]
    private static partial Regex J_MusicNote();

    // === NUMBER 1 AS I ===

    // Standalone 1 as I: "If 1 didn't" -> "If I didn't"
    [GeneratedRegex(@"(?<before>\s)1(?<after>\s)", RegexOptions.Multiline)]
    private static partial Regex Number1_AsI();

    // === SYMBOLS AS LETTERS ===

    // ! as I between words: "do! thrill" -> "do I thrill"
    [GeneratedRegex(@"(?<before>\s)!(?<after>\s+[a-z])", RegexOptions.Multiline)]
    private static partial Regex Exclamation_AsI();

    // @ as 'a': "...@ bottle" -> "...a bottle"
    [GeneratedRegex(@"(?<before>[\s.])@(?<after>\s)", RegexOptions.Multiline)]
    private static partial Regex AtSign_AsA();

    // === PIPE/SLASH AS 'I' ===

    // Contractions: |'m, /'ll, |'ve, /'d -> I'm, I'll, I've, I'd
    [GeneratedRegex(@"[|/]'(?=[mldvsMDLVS])")]
    private static partial Regex PipeSlashContraction();

    // Standalone | or / as a word
    [GeneratedRegex(@"(?<before>^|[\s\p{P}])[|/](?<after>[\s\p{P}]|$)", RegexOptions.Multiline)]
    private static partial Regex PipeSlashStandalone();

    // | or / at word start followed by lowercase letter (It, In, Is, If, etc.)
    [GeneratedRegex(@"(?<before>^|[\s\p{P}])[|/](?<letter>[a-z])", RegexOptions.Multiline)]
    private static partial Regex PipeSlashWordStart();

    // | or / followed by space (likely meant to be "I ")
    [GeneratedRegex(@"(?<before>^|[\s\p{P}])[|/]\s+(?=[a-zA-Z])", RegexOptions.Multiline)]
    private static partial Regex PipeSlashBeforeSpace();

    // === PIPE/SLASH AS 'l' (lowercase L) ===

    // | or / between lowercase letters: wi||ing -> willing
    [GeneratedRegex(@"(?<=[a-z])[|/](?=[a-z])")]
    private static partial Regex PipeSlashMidWord();

    // | or / at end of word after lowercase: gir| -> girl
    [GeneratedRegex(@"(?<letter>[a-z])[|/](?=[\s\p{P}]|$)")]
    private static partial Regex PipeSlashWordEnd();

    // === LOWERCASE L AS 'I' ===

    // l'm, l'll, l've, l'd -> I'm, I'll, I've, I'd
    [GeneratedRegex(@"(?<before>^|[\s\p{P}])l'(?<suffix>[mldvsMDLVS])", RegexOptions.Multiline)]
    private static partial Regex LowercaseL_Contractions();

    // lt, lt's -> It, It's
    [GeneratedRegex(@"(?<before>^|[\s\p{P}])lt(?<suffix>'?s?(?=[\s\p{P}]|$))", RegexOptions.Multiline)]
    private static partial Regex LowercaseL_It();

    // ln -> In
    [GeneratedRegex(@"(?<before>^|[\s\p{P}])ln(?<suffix>[\s\p{P}]|$)", RegexOptions.Multiline)]
    private static partial Regex LowercaseL_In();

    // lf -> If
    [GeneratedRegex(@"(?<before>^|[\s\p{P}])lf(?<suffix>[\s\p{P}]|$)", RegexOptions.Multiline)]
    private static partial Regex LowercaseL_If();

    // ls -> Is
    [GeneratedRegex(@"(?<before>^|[\s\p{P}])ls(?<suffix>[\s\p{P}]|$)", RegexOptions.Multiline)]
    private static partial Regex LowercaseL_Is();

    // === ELLIPSIS FIXES ===

    // .. followed directly by capital letter: "..In" -> "...In"
    [GeneratedRegex(@"\.\.([A-Za-z])")]
    private static partial Regex DoubleDotNoSpace();

    // === ZERO/O CONFUSION ===

    // 0 at word start followed by lowercase
    [GeneratedRegex(@"(?<before>^|[\s\p{P}])0(?<letter>[a-z])", RegexOptions.Multiline)]
    private static partial Regex ZeroAsO();

    // === CLEANUP ===

    // Multiple spaces
    [GeneratedRegex(@"  +")]
    private static partial Regex DoubleSpace();

    // Leading/trailing whitespace on lines
    [GeneratedRegex(@"^[ \t]+|[ \t]+$", RegexOptions.Multiline)]
    private static partial Regex LeadingTrailingSpaces();

    #endregion
}
