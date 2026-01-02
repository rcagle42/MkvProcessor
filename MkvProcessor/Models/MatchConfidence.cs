namespace MkvProcessor.Models;

/// <summary>
/// Confidence level for file-to-episode pattern matching
/// </summary>
public enum MatchConfidence
{
    /// <summary>Exact match found (S01E01 format)</summary>
    High,

    /// <summary>Likely match found (1x01 format)</summary>
    Medium,

    /// <summary>Uncertain match (compact format like 101)</summary>
    Low,

    /// <summary>No recognizable pattern found</summary>
    None
}
