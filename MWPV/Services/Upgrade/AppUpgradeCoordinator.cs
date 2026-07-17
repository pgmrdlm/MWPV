using System;
using System.IO;
using Security.Utility.Storage;
using Security.Utility.Wiping;
using Utilities.Helpers;
using Utilities.Security;
using MWPV.Services.AppLifecycle;
using Backup.Utility;
using System.Reflection;
using MWPV.SqlCatalog;
using Utilities.Sql;

namespace MWPV.Services.Upgrade
{
    public sealed record UpgradeExecutionContext
    {
        public string PasswordDatabasePath { get; init; } = string.Empty;
        public string? PasswordDatabasePassword { get; init; }
        public string KeyFilePath { get; init; } = string.Empty;
        public char[] KeyFilePassword { get; init; } = Array.Empty<char>();
        public string BackupRoot { get; init; } = string.Empty;
        public string? SqlDirectory { get; init; }
        public string? TargetVersion { get; init; }
        public string? UpgradeFlagFilePath { get; init; }
        public string? LogPath { get; init; }
        public string? AppInstallDirectory { get; init; }
        public string? CodeBackupPath { get; init; }
    }

    public sealed class AppUpgradeCoordinator
    {
        private const string SedsKey_KeyPW = "KeyPW";
        private const string SedsKey_KeyFile = "KeyFile";

        private readonly IBackupService _backupService;
        private readonly DbUpgradeVersionReader _versionReader;
        private readonly DbUpgradeExecutor _dbExecutor;
        private readonly KeyFileUpgradeService _keyFileUpgradeService;
        private readonly UpgradeFlagService _flagService;

        public AppUpgradeCoordinator()
            : this(
                new BackupService(),
                new DbUpgradeVersionReader(),
                new DbUpgradeExecutor(),
                new KeyFileUpgradeService(),
                new UpgradeFlagService())
        {
        }

        public AppUpgradeCoordinator(
            IBackupService backupService,
            DbUpgradeVersionReader versionReader,
            DbUpgradeExecutor dbExecutor,
            KeyFileUpgradeService keyFileUpgradeService,
            UpgradeFlagService flagService)
        {
            _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
            _versionReader = versionReader ?? throw new ArgumentNullException(nameof(versionReader));
            _dbExecutor = dbExecutor ?? throw new ArgumentNullException(nameof(dbExecutor));
            _keyFileUpgradeService = keyFileUpgradeService ?? throw new ArgumentNullException(nameof(keyFileUpgradeService));
            _flagService = flagService ?? throw new ArgumentNullException(nameof(flagService));
        }

        public UpgradeResult RunAuthenticatedUpgrade(AppStartupContext startupContext)
        {
            if (startupContext.RunMode != AppRunMode.Upgrade)
                return UpgradeResult.Success("No upgrade requested.");

            char[]? keyPassword = null;
            char[]? dbPasswordChars = null;
            string? dbPassword = null;

            try
            {
                keyPassword = SecureEncryptedDataStore.GetChars(SedsKey_KeyPW);
                dbPasswordChars = SecureEncryptedDataStore.GetChars(DatabaseHelper.DbPasswordKey);
                dbPassword = new string(dbPasswordChars);

                var dbPath = DatabaseHelper.GetAppDbPath();
                var keyFilePath = SecureEncryptedDataStore.GetString(SedsKey_KeyFile);
                var localRoot = Path.GetDirectoryName(dbPath) ?? AppContext.BaseDirectory;
                var backupRoot = Path.Combine(localRoot, "upgrade-backups");
                var logPath = Path.Combine(localRoot, "upgrade", $"upgrade-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log");
                var codeBackupPath = GetDefaultCodeBackupPath();

                var context = new UpgradeExecutionContext
                {
                    PasswordDatabasePath = dbPath,
                    PasswordDatabasePassword = dbPassword,
                    KeyFilePath = keyFilePath,
                    KeyFilePassword = keyPassword,
                    BackupRoot = backupRoot,
                    TargetVersion = startupContext.RequestedTargetVersion,
                    UpgradeFlagFilePath = startupContext.UpgradeFlagFilePath,
                    LogPath = logPath,
                    AppInstallDirectory = AppContext.BaseDirectory,
                    CodeBackupPath = codeBackupPath
                };

                return RunAuthenticatedUpgrade(context);
            }
            catch (Exception ex)
            {
                var fallbackLogPath = Path.Combine(AppContext.BaseDirectory, "upgrade", $"upgrade-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log");
                var fallbackContext = new UpgradeExecutionContext
                {
                    LogPath = fallbackLogPath,
                    AppInstallDirectory = AppContext.BaseDirectory,
                    CodeBackupPath = GetDefaultCodeBackupPath()
                };
                var logger = new UpgradeLogger(fallbackLogPath);
                logger.LogResult(AppExitCode.UpgradeSqlCatalogMissing, "Upgrade context resolution failed.");
                logger.LogManualRecoveryInstructions(
                    fallbackContext,
                    AppExitCode.UpgradeSqlCatalogMissing,
                    AppExitCode.UpgradeSqlCatalogMissing,
                    UpgradeFailureCategory.Unknown,
                    "Upgrade context resolution failed.",
                    backupSet: null,
                    rollback: RollbackResult.NotRequired("Automatic vault-data restore is not performed."));

                return UpgradeResult.Failure(
                    AppExitCode.UpgradeSqlCatalogMissing,
                    AppExitCode.UpgradeSqlCatalogMissing,
                    UpgradeFailureCategory.Unknown,
                    "Upgrade context resolution failed.",
                    logPath: fallbackLogPath,
                    codeBackupPath: fallbackContext.CodeBackupPath,
                    exception: ex);
            }
            finally
            {
                if (keyPassword != null)
                    SensitiveDataCleaner.WipeCharArray(keyPassword);
                if (dbPasswordChars != null)
                    SensitiveDataCleaner.WipeCharArray(dbPasswordChars);
                if (dbPassword != null)
                    SensitiveDataCleaner.WipeString(ref dbPassword);
            }
        }

        public UpgradeResult RunAuthenticatedUpgrade(UpgradeExecutionContext context)
        {
            context = NormalizeContext(context);
            var logger = new UpgradeLogger(context.LogPath);
            BackupDescriptor? backupSet = null;

            try
            {
                logger.LogPhase("Start", "Authenticated upgrade started.");

                var currentVersionForCatalog = _versionReader.ReadCurrentVersion(context.PasswordDatabasePath, context.PasswordDatabasePassword);
                if (!currentVersionForCatalog.Succeeded || string.IsNullOrWhiteSpace(currentVersionForCatalog.Value))
                    return PreBackupFailure(context, currentVersionForCatalog.DetailCode, UpgradeFailureCategory.SqlCatalog, currentVersionForCatalog.Message, currentVersionForCatalog.Exception, logger);
                var catalogResult = TrustedSqlCatalog.LoadAndValidateUpgradeDirectory(
                    context.SqlDirectory ?? DatabaseHelper.GetSqlFolderPath(), currentVersionForCatalog.Value, context.TargetVersion);

                if (!catalogResult.Succeeded || catalogResult.Value == null)
                    return PreBackupFailure(context, AppExitCode.UpgradeSqlCatalogMissing, UpgradeFailureCategory.SqlCatalog,
                        catalogResult.Failures.FirstOrDefault()?.Message ?? "Upgrade SQL package validation failed.", null, logger);

                var catalog = catalogResult.Value;

                logger.LogPhase("Backup", "Creating full vault-data backup set.");
                var backupResult = _backupService.CreateAsync(BuildBackupRequest(context)).GetAwaiter().GetResult();

                if (!backupResult.Succeeded || backupResult.Backup == null)
                {
                    logger.LogResult(AppExitCode.UpgradeBackupSetCreationFailed, backupResult.SafeMessage);
                    logger.LogManualRecoveryInstructions(
                        context,
                        AppExitCode.UpgradeBackupSetCreationFailed,
                        AppExitCode.UpgradeBackupSetCreationFailed,
                        UpgradeFailureCategory.Backup,
                        backupResult.SafeMessage,
                        backupSet: null,
                        rollback: RollbackResult.NotRequired("Automatic vault-data restore is not performed."));
                    return UpgradeResult.Failure(
                        AppExitCode.UpgradeBackupSetCreationFailed,
                        AppExitCode.UpgradeBackupSetCreationFailed,
                        UpgradeFailureCategory.Backup,
                        backupResult.SafeMessage,
                        logPath: context.LogPath,
                        backupSetPath: context.BackupRoot,
                        codeBackupPath: context.CodeBackupPath,
                        exception: null);
                }

                backupSet = backupResult.Backup;
                logger.LogPhase("Backup", $"Backup set created: {backupSet.Manifest.BackupSetId}");

                var executeResult = _dbExecutor.ExecutePlan(
                    context.PasswordDatabasePath,
                    context.PasswordDatabasePassword,
                    catalog);
                if (!executeResult.Succeeded)
                    return CompletePostBackupFailure(executeResult, backupSet, context, logger);

                var expectedVersion = catalog.TargetVersion.ToString();

                var dbValidation = _dbExecutor.ValidateDatabase(
                    context.PasswordDatabasePath,
                    context.PasswordDatabasePassword,
                    expectedVersion);
                if (!dbValidation.Succeeded)
                    return CompletePostBackupFailure(dbValidation, backupSet, context, logger);

                var keyRewrite = _keyFileUpgradeService.RewriteSqlPayload(
                    context.KeyFilePath,
                    context.KeyFilePassword,
                    catalog);
                if (!keyRewrite.Succeeded)
                    return CompletePostBackupFailure(keyRewrite, backupSet, context, logger);

                var keyValidation = _keyFileUpgradeService.ValidateKeyFile(
                    context.KeyFilePath,
                    context.KeyFilePassword,
                    catalog);
                if (!keyValidation.Succeeded)
                    return CompletePostBackupFailure(keyValidation, backupSet, context, logger);

                RuntimeSqlStore.ReplaceVerified(catalog.KeyFilePayloadScripts);

                CleanUpStagedSql(logger);

                var flagClear = _flagService.ClearUpgradeFlag(context.UpgradeFlagFilePath);
                if (!flagClear.Succeeded)
                    logger.LogPhase("FlagClear", flagClear.Message);

                logger.LogResult(AppExitCode.Success, "Upgrade completed successfully.");
                AppLifecycleTransitionService.CompleteSuccessfulUpgrade();
                return UpgradeResult.Success("Upgrade completed successfully.");
            }
            catch (Exception ex)
            {
                if (backupSet != null)
                {
                    var failure = UpgradeStepResult.Failure(
                        "Upgrade",
                        UpgradeFailureCategory.Unknown,
                        AppExitCode.UnknownFatalError,
                        "Upgrade failed unexpectedly.",
                        ex);
                    return CompletePostBackupFailure(failure, backupSet, context, logger);
                }

                return PreBackupFailure(
                    context,
                    AppExitCode.UnknownFatalError,
                    UpgradeFailureCategory.Unknown,
                    "Upgrade failed before backup set creation.",
                    ex,
                    logger);
            }
        }

        private static UpgradeResult PreBackupFailure(
            UpgradeExecutionContext context,
            AppExitCode code,
            UpgradeFailureCategory category,
            string message,
            Exception? exception,
            UpgradeLogger logger)
        {
            CleanUpStagedSql(logger);
            logger.LogResult(code, message);
            logger.LogManualRecoveryInstructions(
                context,
                code,
                code,
                category,
                message,
                backupSet: null,
                rollback: RollbackResult.NotRequired("Automatic vault-data restore is not performed."));
            return UpgradeResult.Failure(
                code,
                code,
                category,
                message,
                logPath: context.LogPath,
                backupSetPath: context.BackupRoot,
                codeBackupPath: context.CodeBackupPath,
                exception: exception);
        }

        private UpgradeResult CompletePostBackupFailure(UpgradeStepResult stepResult, BackupDescriptor backupSet, UpgradeExecutionContext context, UpgradeLogger logger)
        {
            CleanUpStagedSql(logger);
            var rollback = RollbackResult.NotRequired("Automatic vault-data restore is not performed. Use the verified upgrade backup for manual recovery.");

            logger.LogResult(stepResult.DetailCode, stepResult.Message);
            logger.LogManualRecoveryInstructions(
                context,
                stepResult.DetailCode,
                stepResult.DetailCode,
                stepResult.FailureCategory,
                stepResult.Message,
                backupSet,
                rollback);
            return UpgradeResult.Failure(
                stepResult.DetailCode,
                stepResult.DetailCode,
                stepResult.FailureCategory,
                stepResult.Message,
                backupSetCreated: true,
                rollbackRequired: false,
                rollback: rollback,
                logPath: logger.LogPath,
                backupSetPath: backupSet.BackupFolder,
                codeBackupPath: GetDefaultCodeBackupPath(),
                exception: stepResult.Exception);
        }

        private UpgradeResult CompletePostBackupFailure<T>(UpgradeStepResult<T> stepResult, BackupDescriptor backupSet, UpgradeExecutionContext context, UpgradeLogger logger)
        {
            CleanUpStagedSql(logger);
            var rollback = RollbackResult.NotRequired("Automatic vault-data restore is not performed. Use the verified upgrade backup for manual recovery.");

            logger.LogResult(stepResult.DetailCode, stepResult.Message);
            logger.LogManualRecoveryInstructions(
                context,
                stepResult.DetailCode,
                stepResult.DetailCode,
                stepResult.FailureCategory,
                stepResult.Message,
                backupSet,
                rollback);
            return UpgradeResult.Failure(
                stepResult.DetailCode,
                stepResult.DetailCode,
                stepResult.FailureCategory,
                stepResult.Message,
                backupSetCreated: true,
                rollbackRequired: false,
                rollback: rollback,
                logPath: logger.LogPath,
                backupSetPath: backupSet.BackupFolder,
                codeBackupPath: GetDefaultCodeBackupPath(),
                exception: stepResult.Exception);
        }

        private static BackupCreateRequest BuildBackupRequest(UpgradeExecutionContext context) => new()
        {
            BackupRoot = context.BackupRoot,
            FolderPrefix = "MWPV_Backup",
            BackupType = BackupTypes.Upgrade,
            ApplicationName = "MWPV",
            ApplicationVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? string.Empty,
            Files =
            [
                new() { Role = "PasswordDatabase", SourcePath = context.PasswordDatabasePath, DestinationRelativePath = "files/MWPV.db", Required = true },
                new() { Role = "PasswordDatabaseWal", SourcePath = context.PasswordDatabasePath + "-wal", DestinationRelativePath = "files/PasswordDatabase.wal", Required = false },
                new() { Role = "PasswordDatabaseShm", SourcePath = context.PasswordDatabasePath + "-shm", DestinationRelativePath = "files/PasswordDatabase.shm", Required = false },
                new() { Role = "PasswordDatabaseJournal", SourcePath = context.PasswordDatabasePath + "-journal", DestinationRelativePath = "files/PasswordDatabase.journal", Required = false },
                new() { Role = "KeyFileDatabase", SourcePath = context.KeyFilePath, DestinationRelativePath = "files/KeyFileDatabase.pv", Required = true }
            ]
        };

        private static void CleanUpStagedSql(UpgradeLogger logger)
        {
            if (SqlStagingCleanupService.TrySecurelyScrubDefaultStagingFolder(out var cleanupException))
                logger.LogPhase("SqlCleanup", "Staged SQL files deleted.");
            else
                logger.LogPhase("SqlCleanup", $"Staged SQL cleanup failed; original upgrade outcome is unchanged. {cleanupException?.Message}");
        }

        private static UpgradeExecutionContext NormalizeContext(UpgradeExecutionContext context)
        {
            var logPath = string.IsNullOrWhiteSpace(context.LogPath)
                ? Path.Combine(AppContext.BaseDirectory, "upgrade", $"upgrade-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log")
                : context.LogPath;

            return context with
            {
                LogPath = logPath,
                AppInstallDirectory = string.IsNullOrWhiteSpace(context.AppInstallDirectory)
                    ? AppContext.BaseDirectory
                    : context.AppInstallDirectory,
                CodeBackupPath = string.IsNullOrWhiteSpace(context.CodeBackupPath)
                    ? GetDefaultCodeBackupPath()
                    : context.CodeBackupPath
            };
        }

        private static string GetDefaultCodeBackupPath()
        {
            var dataRoot = Path.GetDirectoryName(DatabaseHelper.GetAppDbPath()) ?? AppContext.BaseDirectory;
            return Path.Combine(dataRoot, "Rollback", "code");
        }
    }
}
