namespace MWPV.Services.Upgrade
{
    public enum UpgradeFailureCategory
    {
        None = 0,
        SqlCatalog = 1,
        Backup = 2,
        VersionRead = 3,
        Plan = 4,
        SqlExecution = 5,
        DbValidation = 6,
        KeyFileRewrite = 7,
        KeyFileValidation = 8,
        FlagClear = 9,
        Rollback = 10,
        Unknown = 99
    }
}
