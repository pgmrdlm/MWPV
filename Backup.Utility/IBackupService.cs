namespace Backup.Utility;

public interface IBackupService
{
    Task<BackupCreateResult> CreateAsync(BackupCreateRequest request, CancellationToken cancellationToken = default);
    Task<BackupVerifyResult> VerifyAsync(string backupFolder, CancellationToken cancellationToken = default);
    Task<BackupLoadResult> LoadAsync(string backupFolder, CancellationToken cancellationToken = default);
    Task<BackupRetentionResult> ApplyRetentionAsync(BackupRetentionRequest request, CancellationToken cancellationToken = default);
    Task<BackupRestoreResult> RestoreAsync(BackupRestoreRequest request, CancellationToken cancellationToken = default);
}
