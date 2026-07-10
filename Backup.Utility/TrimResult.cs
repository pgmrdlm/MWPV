namespace Backup.Utility;

public sealed record TrimResult
{
    public TrimStatus Status { get; init; }
    public string SafeMessage { get; init; } = string.Empty;
    public int RequestedRetentionCount { get; init; }
    public IReadOnlyList<string> EligibleFolderNames { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RetainedFolderNames { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> DeletedFolderNames { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> SkippedFolderNames { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> FailedDeletionFolderNames { get; init; } = Array.Empty<string>();

    public bool Succeeded => Status is TrimStatus.Success or TrimStatus.SuccessWithSkippedFolders;
}
