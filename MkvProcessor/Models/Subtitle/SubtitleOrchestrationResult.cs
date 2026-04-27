using MkvProcessor.Services.Subtitle;

namespace MkvProcessor.Models.Subtitle;

/// <summary>
/// Final result of running the subtitle orchestrator on a single source. Contains the winning
/// candidate (if any) plus all candidates that were attempted — the compare UI uses AllCandidates
/// to show the user every strategy's result side-by-side.
/// </summary>
public sealed record SubtitleOrchestrationResult(
    SubtitleCandidate? Winner,
    IReadOnlyList<SubtitleCandidate> AllCandidates,
    SubtitleSourceDescriptor Source)
{
    /// <summary>True when at least one strategy produced a usable result.</summary>
    public bool HasWinner => Winner is not null && Winner.IsViable;

    /// <summary>Path to the winning SRT file, or null if no strategy succeeded.</summary>
    public string? WinnerPath => Winner?.FilePath;
}
