using MkvProcessor.Models.Subtitle;

namespace MkvProcessor.Services.Subtitle;

/// <summary>
/// Runs a chain of subtitle extraction strategies against a single source, validates and scores
/// each candidate, and returns the winner. Strategies execute in priority order; in fast-mode
/// the chain short-circuits on the first result meeting <c>minAcceptableScore</c>, in thorough
/// mode all applicable strategies run so the compare UI can show the full candidate set.
/// </summary>
public class SubtitleOrchestrator
{
    private readonly IReadOnlyList<ISubtitleExtractionStrategy> _strategies;

    /// <summary>Raised for each informational log line — wire to the view-model log panel.</summary>
    public event Action<string>? LogOutput;

    public SubtitleOrchestrator(IEnumerable<ISubtitleExtractionStrategy> strategies)
    {
        _strategies = strategies.ToList();
    }

    /// <summary>
    /// Runs the strategy chain against a single source. Missing tools are skipped silently
    /// (a strategy with IsAvailable=false is never invoked). Strategies that CanHandle=false
    /// are also skipped.
    /// </summary>
    public async Task<SubtitleOrchestrationResult> ExtractAsync(
        SubtitleSourceDescriptor source,
        string outputDirectory,
        string outputBaseName,
        string ocrLanguage,
        string? tessdataPath,
        SubtitleScoringContext scoringContext,
        int minAcceptableScore,
        bool thoroughMode,
        CancellationToken cancellationToken)
    {
        var request = new SubtitleStrategyRequest(
            source, outputDirectory, outputBaseName, ocrLanguage, tessdataPath);

        var candidates = new List<SubtitleCandidate>();

        foreach (var strategy in _strategies)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!strategy.IsAvailable)
            {
                LogOutput?.Invoke($"[{strategy.Name}] skipped (not installed)");
                continue;
            }

            if (!strategy.CanHandle(source))
                continue;

            LogOutput?.Invoke($"[{strategy.Name}] running on {source.CodecName} track...");

            SubtitleStrategyResult result;
            try
            {
                result = await strategy.RunAsync(request, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogOutput?.Invoke($"[{strategy.Name}] threw: {ex.Message}");
                candidates.Add(SubtitleCandidate.Failed(strategy.Name, ex.Message, TimeSpan.Zero));
                continue;
            }

            if (!result.Success || string.IsNullOrEmpty(result.OutputPath))
            {
                LogOutput?.Invoke($"[{strategy.Name}] failed: {result.ErrorMessage}");
                candidates.Add(SubtitleCandidate.Failed(
                    strategy.Name, result.ErrorMessage ?? "unknown failure", result.Duration));
                continue;
            }

            var report = SubtitleValidator.Validate(result.OutputPath);
            var score = SubtitleQualityScorer.Score(report, scoringContext);

            var candidate = new SubtitleCandidate(
                StrategyName: strategy.Name,
                FilePath: result.OutputPath,
                Score: score,
                CueCount: report.CueCount,
                Issues: report.Issues,
                Duration: result.Duration);

            candidates.Add(candidate);
            LogOutput?.Invoke(
                $"[{strategy.Name}] score={score} cues={report.CueCount} in {result.Duration.TotalSeconds:F1}s");

            if (!thoroughMode && score >= minAcceptableScore)
            {
                LogOutput?.Invoke($"[{strategy.Name}] meets threshold ({minAcceptableScore}); stopping chain");
                break;
            }
        }

        var winner = candidates
            .Where(c => c.IsViable)
            .OrderByDescending(c => c.Score)
            .FirstOrDefault();

        if (winner is not null)
            LogOutput?.Invoke($"Winner: {winner.StrategyName} (score {winner.Score})");
        else
            LogOutput?.Invoke("No strategy produced a viable subtitle");

        return new SubtitleOrchestrationResult(winner, candidates, source);
    }
}
