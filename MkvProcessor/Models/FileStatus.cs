namespace MkvProcessor.Models;

/// <summary>
/// Represents the processing status of an MKV file
/// </summary>
public enum FileStatus
{
    Pending,
    Processing,
    Complete,
    Error,
    Skipped
}
