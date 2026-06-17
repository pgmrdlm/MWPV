namespace MWPV.Services.AppLifecycle
{
    public enum AppExitCode
    {
        Success = 0,

        UserCancelledLogin = 10,
        AuthFailed = 11,

        FreshInstallFailed = 20,

        StartupKeyFileInvalid = 30,
        StartupDatabaseOpenFailed = 31,
        StartupSqlCatalogMissing = 32,

        UpgradeSqlCatalogMissing = 100,
        UpgradeBackupSetCreationFailed = 101,
        UpgradeCurrentVersionReadFailed = 102,
        UpgradePlanInvalid = 103,
        UpgradeSqlExecutionFailed = 110,
        UpgradeDbValidationFailed = 120,

        UpgradeKeyFileRewriteFailed = 210,
        UpgradeKeyFileValidationFailed = 220,
        UpgradeFailedRollbackSucceeded = 250,

        UpgradeRollbackFailed = 300,

        UnhandledFatalError = 900,
        UnknownFatalError = 999
    }
}
