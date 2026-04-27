namespace MkvProcessor.Models.Subtitle;

/// <summary>
/// A scored subtitle extraction result. Produced by the orchestrator after running a strategy,
/// validating its output, and scoring the result. Multiple candidates compete per source; the
/// highest-scoring one becomes the winner.
/// </summary>
public sealed record SubtitleCandidate(
    string StrategyName,
    string FilePath,
    int Score,
    int CueCount,
    IReadOnlyList<string> Issues,
    TimeSpan Duration)
{
    /// <summary>
    /// A sentinel candidate representing a strategy that failed to produce any output.
    /// Kept so the compare UI can show why a strategy was unsuccessful.
    /// </summary>
    public static SubtitleCandidate Failed(string strategyName, string reason, TimeSpan duration) =>
        new(strategyName, string.Empty, 0, 0, new[] { reason }, duration);

    public bool IsViable => !string.IsNullOrEmpty(FilePath) && Score > 0;
}
