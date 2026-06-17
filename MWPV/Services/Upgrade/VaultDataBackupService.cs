using MWPV.Services.AppLifecycle;

namespace MWPV.Services.Upgrade
{
    public sealed class VaultDataBackupService
    {
        public OperationResult CreateBackupSetPlaceholder() =>
            OperationResult.Failure(
                AppExitCode.UpgradeBackupSetCreationFailed,
                "Backup set creation is not implemented yet.");

        public OperationResult VerifyBackupSet(UpgradeBackupSet backupSet) =>
            OperationResult.Success("Backup set verification skeleton only.");

        public RollbackResult RestoreBackupSet(UpgradeBackupSet backupSet) =>
            RollbackResult.NotRequired("Backup set restore is not implemented yet.");
    }
}
