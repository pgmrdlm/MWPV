namespace Backup.Utility.Internal;

internal readonly record struct RobocopyMapping(BackupStatus Status, bool FilesCopied, bool WarningsOccurred, bool CanPublish);

internal static class RobocopyExitCodeMapper
{
    internal static RobocopyMapping Map(int exitCode)
    {
        if (exitCode < 0)
            return new(BackupStatus.FatalFailure, false, false, false);
        if (exitCode == 0)
            return new(BackupStatus.SuccessNoChanges, false, false, true);
        if (exitCode == 1)
            return new(BackupStatus.SuccessCopied, true, false, true);
        if (exitCode <= 3)
            return new(BackupStatus.SuccessWithWarnings, (exitCode & 1) != 0, true, true);
        if (exitCode <= 7)
            return new(BackupStatus.Incomplete, (exitCode & 1) != 0, true, false);
        if (exitCode <= 15)
            return new(BackupStatus.Failed, (exitCode & 1) != 0, true, false);
        return new(BackupStatus.FatalFailure, (exitCode & 1) != 0, true, false);
    }
}
