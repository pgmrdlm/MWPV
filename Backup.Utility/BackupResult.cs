namespace Backup.Utility;

public sealed record BackupResult
{
    public BackupStatus Status { get; init; }
    public string SafeMessage { get; init; } = string.Empty;
    public string ResolvedFolderName { get; init; } = string.Empty;
    public string ResolvedFullPath { get; init; } = string.Empty;
    public int? RobocopyExitCode { get; init; }
    public bool FilesCopied { get; init; }
    public bool WarningsOccurred { get; init; }
    public bool CleanupAttempted { get; init; }
    public bool CleanupSucceeded { get; init; }
    public string? IncompleteFolderName { get; init; }

    public bool Succeeded => Status is BackupStatus.SuccessNoChanges
        or BackupStatus.SuccessCopied
        or BackupStatus.SuccessWithWarnings;
}
