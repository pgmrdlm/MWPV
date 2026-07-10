namespace Backup.Utility;

public enum TrimStatus
{
    Success,
    SuccessWithSkippedFolders,
    PartialFailure,
    InvalidRequest,
    FolderUnavailable,
    AmbiguousBackupFamily,
    Canceled,
    Failed
}
