namespace Backup.Utility;

public enum BackupStatus
{
    SuccessNoChanges,
    SuccessCopied,
    SuccessWithWarnings,
    InvalidRequest,
    SourceUnavailable,
    DestinationUnavailable,
    DestinationCollision,
    RobocopyUnavailable,
    ProcessLaunchFailed,
    Canceled,
    Incomplete,
    Failed,
    FatalFailure,
    CleanupFailed
}
