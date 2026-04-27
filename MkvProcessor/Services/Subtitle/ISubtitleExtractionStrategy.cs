using MkvProcessor.Services;

namespace MkvProcessor.Services.Subtitle;

/// <summary>
/// Whether the subtitle source is a stream inside an MKV container or a standalone file on disk.
/// </summary>
public enum SubtitleSourceKind
{
    MkvStream,
    StandaloneFile
}

/// <summary>
/// Broad classification of a subtitle codec. Drives strategy routing: text codecs are passthrough,
/// bitmap codecs require OCR, and VobSub needs special handling for its .idx+.sub pair.
/// </summary>
public enum SubtitleCodecClass
{
    /// <summary>Text-based: subrip, ass, ssa, mov_text, webvtt — extract directly as .srt/.ass.</summary>
    Text,

    /// <summary>HDMV PGS bitmap subtitles (Blu-ray). Extracted as .sup, requires OCR.</summary>
    PgsBitmap,

    /// <summary>DVD VobSub bitmap subtitles. Extracted as .idx + .sub pair, requires OCR.</summary>
    VobSubBitmap,

    /// <summary>DVB bitmap subtitles (broadcast). Requires specialized OCR.</summary>
    DvbBitmap,

    /// <summary>DVB teletext subtitles. Text-based but needs specialized extraction.</summary>
    Teletext,

    /// <summary>Codec not recognized — strategies decide whether to attempt extraction.</summary>
    Unknown
}

/// <summary>
/// Describes an input subtitle source — either an MKV stream or a standalone file.
/// Immutable; constructed once per extraction job and passed through the strategy chain.
/// </summary>
public sealed record SubtitleSourceDescriptor(
    SubtitleSourceKind Kind,
    string SourcePath,
    int StreamIndex,
    string CodecName,
    string Language,
    SubtitleCodecClass CodecClass)
{
    /// <summary>
    /// Classifies an FFprobe codec name into a broad codec class for strategy routing.
    /// Mirrors the text/bitmap detection in FFmpegService but adds finer-grained categories.
    /// </summary>
    public static SubtitleCodecClass ClassifyCodec(string codecName)
    {
        if (string.IsNullOrWhiteSpace(codecName))
            return SubtitleCodecClass.Unknown;

        var c = codecName.Trim().ToLowerInvariant();

        if (c is "subrip" or "srt" or "ass" or "ssa" or "mov_text" or "webvtt" or "text")
            return SubtitleCodecClass.Text;

        if (c.Contains("pgs") || c.Contains("hdmv"))
            return SubtitleCodecClass.PgsBitmap;

        if (c is "dvd_subtitle" or "dvdsub" or "vobsub")
            return SubtitleCodecClass.VobSubBitmap;

        if (c is "dvb_teletext" or "teletext")
            return SubtitleCodecClass.Teletext;

        if (c.Contains("dvb") || c == "xsub")
            return SubtitleCodecClass.DvbBitmap;

        return SubtitleCodecClass.Unknown;
    }

    /// <summary>
    /// Builds a descriptor for a subtitle stream inside an MKV file.
    /// </summary>
    public static SubtitleSourceDescriptor FromStream(string mkvPath, SubtitleStreamInfo stream)
    {
        return new SubtitleSourceDescriptor(
            Kind: SubtitleSourceKind.MkvStream,
            SourcePath: mkvPath,
            StreamIndex: stream.Index,
            CodecName: stream.CodecName,
            Language: stream.Language,
            CodecClass: ClassifyCodec(stream.CodecName));
    }

    /// <summary>
    /// Builds a descriptor for a standalone subtitle file on disk. Codec class is inferred
    /// from the file extension.
    /// </summary>
    public static SubtitleSourceDescriptor FromStandaloneFile(string filePath, string language = "und")
    {
        var ext = System.IO.Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
        var (codecName, codecClass) = ext switch
        {
            "sup" => ("hdmv_pgs", SubtitleCodecClass.PgsBitmap),
            "sub" => ("dvd_subtitle", SubtitleCodecClass.VobSubBitmap),
            "idx" => ("dvd_subtitle", SubtitleCodecClass.VobSubBitmap),
            "srt" => ("subrip", SubtitleCodecClass.Text),
            "ass" => ("ass", SubtitleCodecClass.Text),
            "ssa" => ("ssa", SubtitleCodecClass.Text),
            _ => (ext, SubtitleCodecClass.Unknown)
        };

        return new SubtitleSourceDescriptor(
            Kind: SubtitleSourceKind.StandaloneFile,
            SourcePath: filePath,
            StreamIndex: -1,
            CodecName: codecName,
            Language: language,
            CodecClass: codecClass);
    }
}

/// <summary>
/// Input to a strategy's RunAsync call. Contains the source plus OCR/output parameters.
/// </summary>
public sealed record SubtitleStrategyRequest(
    SubtitleSourceDescriptor Source,
    string OutputDirectory,
    string OutputBaseName,
    string Language,
    string? TessdataPath);

/// <summary>
/// Result of running a single strategy. Orchestrator collects these and hands them to the
/// validator/scorer to produce a final SubtitleCandidate.
/// </summary>
public sealed record SubtitleStrategyResult(
    bool Success,
    string? OutputPath,
    string? ErrorMessage,
    TimeSpan Duration,
    IReadOnlyList<string> LogLines)
{
    public static SubtitleStrategyResult Failure(string error, TimeSpan duration, IReadOnlyList<string>? logs = null) =>
        new(false, null, error, duration, logs ?? Array.Empty<string>());
}

/// <summary>
/// Contract for any subtitle extraction or OCR strategy. Implementations wrap a specific tool
/// (mkvextract, Subtitle Edit, PgsToSrt, etc.) and expose a uniform interface to the orchestrator.
/// </summary>
public interface ISubtitleExtractionStrategy
{
    /// <summary>Human-readable strategy name, shown in logs and the compare UI.</summary>
    string Name { get; }

    /// <summary>True if the underlying tool is installed and discoverable.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Whether this strategy can produce output from the given source. Used by the orchestrator
    /// to skip incompatible strategies (e.g., PgsToSrt can't handle VobSub).
    /// </summary>
    bool CanHandle(SubtitleSourceDescriptor source);

    /// <summary>
    /// Runs the strategy and produces an SRT (or intermediate file) at the requested location.
    /// Must not throw on expected failures — return a failure result instead.
    /// </summary>
    Task<SubtitleStrategyResult> RunAsync(SubtitleStrategyRequest request, CancellationToken cancellationToken);
}
