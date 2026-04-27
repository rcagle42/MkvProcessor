using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Text;
using MkvProcessor.Services;
using Nikse.SubtitleEdit.Core.Dictionaries;
using Nikse.SubtitleEdit.Core.VobSub;
using TesseractOCR;
using TesseractOCR.Enums;
using DrawingImageFormat = System.Drawing.Imaging.ImageFormat;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using TesseractImage = TesseractOCR.Pix.Image;

namespace MkvProcessor.Services.Subtitle.Strategies;

/// <summary>
/// Fully in-process VobSub (.idx + .sub) to SRT conversion. Parses the pair with libse's
/// <c>VobSubParser</c>, renders each subtitle frame to a <see cref="Bitmap"/>, runs it
/// through <c>TesseractOCR.Engine</c>, and writes an SRT. No external process, no GUI.
///
/// This covers the DVD subtitle case (codec <c>dvd_subtitle</c>) that neither FFmpeg nor
/// PgsToSrt can turn into text on their own, which was the gap that motivated this strategy.
/// </summary>
public class VobSubEmbeddedOcrStrategy : ISubtitleExtractionStrategy
{
    private readonly MkvExtractService _mkvExtract;

    public VobSubEmbeddedOcrStrategy(MkvExtractService mkvExtract)
    {
        _mkvExtract = mkvExtract;
    }

    public string Name => "VobSub OCR (embedded)";

    /// <summary>
    /// Always reports available — the bundled libse + TesseractOCR DLLs are referenced by
    /// the project and resolved at runtime via the App-level AssemblyResolve hook. If those
    /// DLLs are missing for any reason, the first call will surface a clear error rather
    /// than silently being skipped here.
    /// </summary>
    public bool IsAvailable => true;

    public bool CanHandle(SubtitleSourceDescriptor source) =>
        source.CodecClass == SubtitleCodecClass.VobSubBitmap;

    public async Task<SubtitleStrategyResult> RunAsync(
        SubtitleStrategyRequest request,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var logs = new List<string>();
        var source = request.Source;

        Directory.CreateDirectory(request.OutputDirectory);

        // Step 1: obtain a .idx + .sub pair on disk. For standalone sources the user has
        // already pointed us at one of the two; for MKV sources we demux via mkvextract
        // to a temp .idx path (mkvextract writes both files automatically).
        string idxPath, subPath;
        bool pairIsTemp;

        if (source.Kind == SubtitleSourceKind.StandaloneFile)
        {
            (idxPath, subPath) = LocateStandalonePair(source.SourcePath);
            if (!File.Exists(idxPath) || !File.Exists(subPath))
            {
                sw.Stop();
                return SubtitleStrategyResult.Failure(
                    "VobSub requires both .idx and .sub files sitting side-by-side", sw.Elapsed, logs);
            }
            pairIsTemp = false;
        }
        else
        {
            idxPath = Path.Combine(request.OutputDirectory, $"{request.OutputBaseName}.vobsubocr.idx");
            subPath = Path.ChangeExtension(idxPath, ".sub");
            pairIsTemp = true;

            if (!_mkvExtract.IsAvailable)
            {
                sw.Stop();
                return SubtitleStrategyResult.Failure(
                    "mkvextract required to demux VobSub from MKV — install MKVToolNix or bundle mkvextract.exe",
                    sw.Elapsed, logs);
            }

            logs.Add($"[mkvextract] extracting VobSub track {source.StreamIndex} → {Path.GetFileName(idxPath)}");
            var extract = await _mkvExtract.ExtractTrackAsync(
                source.SourcePath, source.StreamIndex, idxPath, cancellationToken);
            if (!extract.Success || !File.Exists(subPath))
            {
                sw.Stop();
                TryDelete(idxPath);
                TryDelete(subPath);
                return SubtitleStrategyResult.Failure(
                    $"mkvextract failed: {extract.ErrorMessage}", sw.Elapsed, logs);
            }
        }

        // Step 2: OCR the pair on a background thread so we don't block the UI. The libse
        // parser and TesseractOCR engine are both synchronous, and this strategy may run
        // for tens of seconds on a feature-length DVD track.
        var outputPath = Path.Combine(
            request.OutputDirectory, $"{request.OutputBaseName}.vobsubocr.srt");

        try
        {
            await Task.Run(() => OcrPairToSrt(
                idxPath, subPath, outputPath, request.Language, request.TessdataPath, logs, cancellationToken),
                cancellationToken);

            // Run the shared OCR-error post-processor (same one PgsToSrtService applies).
            // Fixes common Tesseract artifacts: pipe/slash → I/l, lone 1 → I, trailing
            // music-symbol glyphs, etc. Significant quality improvement for free.
            if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 0)
            {
                logs.Add("[post] applying SrtPostProcessor OCR corrections");
                await SrtPostProcessor.ProcessFileAsync(outputPath);
            }
        }
        catch (OperationCanceledException)
        {
            TryDelete(outputPath);
            if (pairIsTemp) { TryDelete(idxPath); TryDelete(subPath); }
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            if (pairIsTemp) { TryDelete(idxPath); TryDelete(subPath); }
            return SubtitleStrategyResult.Failure($"VobSub OCR failed: {ex.Message}", sw.Elapsed, logs);
        }
        finally
        {
            if (pairIsTemp) { TryDelete(idxPath); TryDelete(subPath); }
        }

        sw.Stop();

        if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
            return SubtitleStrategyResult.Failure("OCR produced no output", sw.Elapsed, logs);

        return new SubtitleStrategyResult(true, outputPath, null, sw.Elapsed, logs);
    }

    /// <summary>
    /// Runs the libse VobSub parser + TesseractOCR pipeline synchronously, writing SRT
    /// cues as they're produced. Cancellation is checked between frames so the loop can
    /// bail quickly on a user cancel.
    /// </summary>
    private static void OcrPairToSrt(
        string idxPath,
        string subPath,
        string outputPath,
        string language,
        string? tessdataPath,
        List<string> logs,
        CancellationToken cancellationToken)
    {
        var isPal = DetectPalFromIdx(idxPath);
        logs.Add($"[libse] parsing VobSub pair (isPal={isPal})");

        var parser = new VobSubParser(isPal);
        parser.OpenSubIdx(subPath, idxPath);
        var packs = parser.MergeVobSubPacks();
        logs.Add($"[libse] {packs.Count} subtitle frame(s) to OCR");

        // Resolve tessdata location — fall back to PgsToSrt's bundled tessdata when the
        // user hasn't configured one explicitly, since it's guaranteed to exist.
        var tessdata = ResolveTessdataPath(tessdataPath);
        if (tessdata is null)
            throw new InvalidOperationException(
                "Tesseract language data directory not found. Configure Tessdata Path in the Subtitle Converter tab.");

        using var engine = new Engine(tessdata, language, EngineMode.Default);

        // Assemble the three-layer word corrector: SE's curated WordReplaceList, our
        // bundled top-10k English word list, and a fixed OCR confusion table. Each layer
        // catches a different class of mistake — see SubtitleWordCorrector for details.
        var seList = LoadOcrFixReplaceList(language, logs);
        // Comprehensive ~370k-word English list from dwyl/english-words. Using the big list
        // (vs a curated top-10k) catches mid-frequency words like "gulp", "whimper", and
        // "whine" that subtitle OCR routinely needs, and it also happens to include proper
        // nouns like "Muriel" and "Bagge" which protects them from accidental correction.
        var wordListPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "dictionaries", "english_words.txt");
        var corrector = new SubtitleWordCorrector(wordListPath, seList);
        logs.Add($"[ocrfix] word corrector ready (SE replace entries: {(seList is null ? 0 : 2798)}, dict: {Path.GetFileName(wordListPath)})");

        var sb = new StringBuilder();
        int cueIndex = 1;

        foreach (var pack in packs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Bitmap? rawBitmap = null;
            Bitmap? upscaled = null;
            try
            {
                // Render with forced colors instead of the DVD's native palette. Tesseract
                // wants dark-on-light and gets confused by light-on-dark, so we map the
                // VobSub 4-entry palette onto just two colors: pattern (letter body) →
                // black, everything else → white.
                //
                // Critical detail: we treat the emphasis colors (outline + shadow) as
                // BACKGROUND, not as part of the letter. Earlier revisions painted them
                // black along with the pattern and it worked fine for upright S1 subtitles
                // — but on S4 italic subtitles the outlines of adjacent letters overlap
                // horizontally, and merging them all to black made italic text fuse into
                // continuous blobs (see the _ocrdebug/ dumps). Keeping just the thin
                // pattern core produces non-overlapping letter cores even under italic
                // slant.
                //
                // The 7-arg overload takes: (palette CLUT, bg, pattern, emphasis1,
                // emphasis2, useCustomColors, crop).
                rawBitmap = pack.SubPicture?.GetBitmap(
                    pack.Palette,
                    background: Color.White,
                    pattern: Color.Black,
                    emphasis1: Color.White,
                    emphasis2: Color.White,
                    useCustomColors: true,
                    crop: true);
                if (rawBitmap is null) continue;

                // Upscale 3× with bicubic interpolation. VobSub rasters are ~720×60 with a
                // text x-height of ~20 pixels — well below Tesseract's sweet spot of 30–40.
                // Pushing the letters to ~60 px x-height dramatically improves recognition
                // accuracy on subtitle-sized inputs at a modest CPU cost. A previous
                // revision also thresholded post-upscale to pure B&W, but that eroded thin
                // letter strokes (l, i, t) to the point where whole characters vanished —
                // Tesseract's internal binarization handles the soft edges better.
                upscaled = UpscaleBitmap(rawBitmap, scale: 3);

                string text;
                using (var ms = new MemoryStream())
                {
                    upscaled.Save(ms, DrawingImageFormat.Png);
                    ms.Position = 0;
                    using var pix = TesseractImage.LoadFromMemory(ms.ToArray());
                    // SingleBlock tells Tesseract "this whole image is one block of text" —
                    // Auto mode often misclassifies tiny subtitle images and returns empty
                    // or garbled output.
                    using var page = engine.Process(pix, PageSegMode.SingleBlock);
                    text = page.Text?.Trim() ?? string.Empty;
                }

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                // Run the per-word corrector. This is where middie→middle, guip→gulp,
                // iver→liver, K's→It's, etc. get fixed using the three-layer algorithm.
                try { text = corrector.CorrectText(text); }
                catch { /* correction errors are never fatal — keep the raw OCR text */ }

                sb.Append(cueIndex).Append('\n');
                sb.Append(FormatSrtTimestamp(pack.StartTime)).Append(" --> ")
                  .Append(FormatSrtTimestamp(pack.EndTime)).Append('\n');
                sb.Append(text).Append("\n\n");
                cueIndex++;
            }
            finally
            {
                upscaled?.Dispose();
                rawBitmap?.Dispose();
            }
        }

        File.WriteAllText(outputPath, sb.ToString());
        logs.Add($"[libse] wrote {cueIndex - 1} SRT cues to {Path.GetFileName(outputPath)}");
    }

    /// <summary>
    /// Scales a bitmap up with high-quality bicubic interpolation. Used to push subtitle
    /// letters from ~20 px to ~60 px x-height before handing them to Tesseract.
    /// </summary>
    private static Bitmap UpscaleBitmap(Bitmap source, int scale)
    {
        var destination = new Bitmap(
            source.Width * scale, source.Height * scale, DrawingPixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(destination);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(source, 0, 0, destination.Width, destination.Height);
        return destination;
    }

    /// <summary>
    /// Loads Subtitle Edit's bundled OCR fix replace list for the given language, or
    /// returns null if the language isn't bundled. The XML files live in the app's
    /// <c>dictionaries/</c> folder and follow SE's naming convention
    /// (<c>{lang}_OCRFixReplaceList.xml</c>).
    /// </summary>
    private static OcrFixReplaceList? LoadOcrFixReplaceList(string language, List<string> logs)
    {
        try
        {
            var dictFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dictionaries");
            var xmlPath = Path.Combine(dictFolder, $"{language}_OCRFixReplaceList.xml");
            if (!File.Exists(xmlPath))
            {
                logs.Add($"[ocrfix] no dictionary bundled for {language} — skipping correction pass");
                return null;
            }

            var list = new OcrFixReplaceList(xmlPath);
            logs.Add($"[ocrfix] loaded {language} replace list from {Path.GetFileName(xmlPath)}");
            return list;
        }
        catch (Exception ex)
        {
            logs.Add($"[ocrfix] failed to load replace list: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Parses the .idx header to determine whether the disc is PAL (720×576, 25 fps) or
    /// NTSC (720×480, 29.97 fps). libse's <see cref="VobSubParser"/> uses this flag to
    /// interpret frame timestamps correctly — getting it wrong produces cues with badly
    /// scaled timing.
    /// </summary>
    private static bool DetectPalFromIdx(string idxPath)
    {
        try
        {
            foreach (var line in File.ReadLines(idxPath))
            {
                if (line.StartsWith("size:", StringComparison.OrdinalIgnoreCase))
                {
                    if (line.Contains("576")) return true;
                    if (line.Contains("480")) return false;
                }
            }
        }
        catch { }
        return true; // PAL is the most common default; bad timing is recoverable, no output is not.
    }

    /// <summary>
    /// Given a user-provided file path (which might be the .idx or the .sub), returns the
    /// matching pair. Callers validate existence afterward.
    /// </summary>
    private static (string idxPath, string subPath) LocateStandalonePair(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext == ".idx"
            ? (path, Path.ChangeExtension(path, ".sub"))
            : (Path.ChangeExtension(path, ".idx"), path);
    }

    private static string? ResolveTessdataPath(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
            return configured;

        // Fall back to PgsToSrt's bundled tessdata, which we know ships with the app.
        var fallback = PgsToSrtLocator.FindTessdata();
        if (!string.IsNullOrWhiteSpace(fallback) && Directory.Exists(fallback))
            return fallback;

        return null;
    }

    private static string FormatSrtTimestamp(TimeSpan t) =>
        string.Format(CultureInfo.InvariantCulture,
            "{0:D2}:{1:D2}:{2:D2},{3:D3}",
            (int)t.TotalHours, t.Minutes, t.Seconds, t.Milliseconds);

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
