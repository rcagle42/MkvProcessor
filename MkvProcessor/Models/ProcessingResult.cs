namespace MkvProcessor.Models;

/// <summary>
/// Result of processing a single file
/// </summary>
public class ProcessingResult
{
    /// <summary>
    /// The file that was processed
    /// </summary>
    public required MkvFile File { get; init; }

    /// <summary>
    /// Whether processing succeeded
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Error message if processing failed
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Path to the output file (if successful)
    /// </summary>
    public string? OutputPath { get; init; }

    /// <summary>
    /// Size of the output file in bytes
    /// </summary>
    public long OutputSize { get; init; }

    /// <summary>
    /// Time taken to process the file
    /// </summary>
    public TimeSpan ProcessingTime { get; init; }

    /// <summary>
    /// Whether the file was skipped (already existed)
    /// </summary>
    public bool Skipped { get; init; }

    /// <summary>
    /// Creates a success result
    /// </summary>
    public static ProcessingResult Successful(MkvFile file, string outputPath, long outputSize, TimeSpan processingTime) => new()
    {
        File = file,
        Success = true,
        OutputPath = outputPath,
        OutputSize = outputSize,
        ProcessingTime = processingTime
    };

    /// <summary>
    /// Creates a failure result
    /// </summary>
    public static ProcessingResult Failed(MkvFile file, string errorMessage, TimeSpan processingTime) => new()
    {
        File = file,
        Success = false,
        ErrorMessage = errorMessage,
        ProcessingTime = processingTime
    };

    /// <summary>
    /// Creates a skipped result
    /// </summary>
    public static ProcessingResult SkippedResult(MkvFile file, string outputPath) => new()
    {
        File = file,
        Success = true,
        Skipped = true,
        OutputPath = outputPath
    };
}
