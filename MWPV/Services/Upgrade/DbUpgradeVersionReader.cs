using MWPV.Services.AppLifecycle;

namespace MWPV.Services.Upgrade
{
    public sealed class DbUpgradeVersionReader
    {
        public UpgradeStepResult ReadCurrentVersionPlaceholder() =>
            UpgradeStepResult.Failure(
                "ReadCurrentVersion",
                UpgradeFailureCategory.VersionRead,
                AppExitCode.UpgradeCurrentVersionReadFailed,
                "Database version reading is not implemented yet.");
    }
}
