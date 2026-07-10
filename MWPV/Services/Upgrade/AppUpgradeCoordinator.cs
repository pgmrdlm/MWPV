using System;
using System.IO;
using Security.Utility.Storage;
using Security.Utility.Wiping;
using Utilities.Helpers;
using Utilities.Security;
using MWPV.Services.AppLifecycle;
using Backup.Utility;
using Microsoft.Data.Sqlite;
using System.Collections.Generic;
using System.Reflection;

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
                var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var backupRoot = Path.Combine(localRoot, "upgrade-backups");
                var logPath = Path.Combine(localRoot, "upgrade", $"upgrade-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log");
                var codeBackupPath = Path.Combine(documentsPath, "MWPV_Rollback", "code");

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
                    CodeBackupPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "MWPV_Rollback",
                        "code")
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
                    rollback: RollbackResult.NotRequired("Upgrade failed before automatic rollback could run."));

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

                var catalogResult = UpgradeSqlCatalog.LoadInstalled(new UpgradeSqlCatalogOptions
                {
                    SqlDirectory = context.SqlDirectory,
                    RequireAtLeastOneUpgradeScript = true,
                    LoadAllNormalSqlFiles = true
                });

                if (!catalogResult.Succeeded || catalogResult.Value == null)
                    return PreBackupFailure(context, catalogResult.Code, UpgradeFailureCategory.SqlCatalog, catalogResult.Message, catalogResult.Exception, logger);

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
                        rollback: RollbackResult.NotRequired("Backup set creation failed before automatic rollback could run."));
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

                var versionResult = _versionReader.ReadCurrentVersion(
                    context.PasswordDatabasePath,
                    context.PasswordDatabasePassword);
                if (!versionResult.Succeeded || string.IsNullOrWhiteSpace(versionResult.Value))
                    return RollbackAfterFailure(versionResult, backupSet, context, logger);

                var planResult = _dbExecutor.BuildPlan(versionResult.Value, catalog, context.TargetVersion);
                if (!planResult.Succeeded || planResult.Value == null)
                    return RollbackAfterFailure(planResult, backupSet, context, logger);

                var plan = planResult.Value;
                var executeResult = _dbExecutor.ExecutePlan(
                    context.PasswordDatabasePath,
                    context.PasswordDatabasePassword,
                    plan,
                    catalog);
                if (!executeResult.Succeeded)
                    return RollbackAfterFailure(executeResult, backupSet, context, logger);

                var expectedVersion = string.IsNullOrWhiteSpace(plan.TargetDbVersion)
                    ? versionResult.Value
                    : plan.TargetDbVersion;

                var dbValidation = _dbExecutor.ValidateDatabase(
                    context.PasswordDatabasePath,
                    context.PasswordDatabasePassword,
                    expectedVersion);
                if (!dbValidation.Succeeded)
                    return RollbackAfterFailure(dbValidation, backupSet, context, logger);

                var keyRewrite = _keyFileUpgradeService.RewriteSqlPayload(
                    context.KeyFilePath,
                    context.KeyFilePassword,
                    catalog);
                if (!keyRewrite.Succeeded)
                    return RollbackAfterFailure(keyRewrite, backupSet, context, logger);

                var keyValidation = _keyFileUpgradeService.ValidateKeyFile(
                    context.KeyFilePath,
                    context.KeyFilePassword,
                    catalog);
                if (!keyValidation.Succeeded)
                    return RollbackAfterFailure(keyValidation, backupSet, context, logger);

                if (SqlStagingCleanupService.TrySecurelyScrubDefaultStagingFolder(out var cleanupException))
                {
                    logger.LogPhase("SqlCleanup", "SQL staging folder cleanup completed.");
                }
                else
                {
                    logger.LogPhase(
                        "SqlCleanup",
                        $"SQL staging folder cleanup failed after successful validation; staged files were left in place. {cleanupException?.Message}");
                }

                var flagClear = _flagService.ClearUpgradeFlag(context.UpgradeFlagFilePath);
                if (!flagClear.Succeeded)
                    logger.LogPhase("FlagClear", flagClear.Message);

                logger.LogResult(AppExitCode.Success, "Upgrade completed successfully.");
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
                    return RollbackAfterFailure(failure, backupSet, context, logger);
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
            logger.LogResult(code, message);
            logger.LogManualRecoveryInstructions(
                context,
                code,
                code,
                category,
                message,
                backupSet: null,
                rollback: RollbackResult.NotRequired("Upgrade failed before automatic rollback could run."));
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

        private UpgradeResult RollbackAfterFailure(UpgradeStepResult stepResult, BackupDescriptor backupSet, UpgradeExecutionContext context, UpgradeLogger logger)
        {
            logger.LogPhase("Rollback", $"Failure after backup at {stepResult.StepName}: {stepResult.Message}");
            var rollback = RestoreBackupSet(backupSet, context);
            var finalCode = rollback.Status == RollbackResultStatus.Succeeded
                ? AppExitCode.UpgradeFailedRollbackSucceeded
                : AppExitCode.UpgradeRollbackFailed;

            logger.LogResult(finalCode, rollback.Message);
            logger.LogManualRecoveryInstructions(
                context,
                finalCode,
                stepResult.DetailCode,
                stepResult.FailureCategory,
                stepResult.Message,
                backupSet,
                rollback);
            return UpgradeResult.Failure(
                finalCode,
                stepResult.DetailCode,
                stepResult.FailureCategory,
                stepResult.Message,
                backupSetCreated: true,
                rollbackRequired: true,
                rollback: rollback,
                logPath: logger.LogPath,
                backupSetPath: backupSet.BackupFolder,
                codeBackupPath: GetDefaultCodeBackupPath(),
                exception: stepResult.Exception);
        }

        private UpgradeResult RollbackAfterFailure<T>(UpgradeStepResult<T> stepResult, BackupDescriptor backupSet, UpgradeExecutionContext context, UpgradeLogger logger)
        {
            logger.LogPhase("Rollback", $"Failure after backup at {stepResult.StepName}: {stepResult.Message}");
            var rollback = RestoreBackupSet(backupSet, context);
            var finalCode = rollback.Status == RollbackResultStatus.Succeeded
                ? AppExitCode.UpgradeFailedRollbackSucceeded
                : AppExitCode.UpgradeRollbackFailed;

            logger.LogResult(finalCode, rollback.Message);
            logger.LogManualRecoveryInstructions(
                context,
                finalCode,
                stepResult.DetailCode,
                stepResult.FailureCategory,
                stepResult.Message,
                backupSet,
                rollback);
            return UpgradeResult.Failure(
                finalCode,
                stepResult.DetailCode,
                stepResult.FailureCategory,
                stepResult.Message,
                backupSetCreated: true,
                rollbackRequired: true,
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
                new() { Role = "PasswordDatabase", SourcePath = context.PasswordDatabasePath, DestinationRelativePath = "files/PasswordDatabase.db", Required = true },
                new() { Role = "PasswordDatabaseWal", SourcePath = context.PasswordDatabasePath + "-wal", DestinationRelativePath = "files/PasswordDatabase.wal", Required = false },
                new() { Role = "PasswordDatabaseShm", SourcePath = context.PasswordDatabasePath + "-shm", DestinationRelativePath = "files/PasswordDatabase.shm", Required = false },
                new() { Role = "PasswordDatabaseJournal", SourcePath = context.PasswordDatabasePath + "-journal", DestinationRelativePath = "files/PasswordDatabase.journal", Required = false },
                new() { Role = "KeyFileDatabase", SourcePath = context.KeyFilePath, DestinationRelativePath = "files/KeyFileDatabase.pv", Required = true }
            ]
        };

        private RollbackResult RestoreBackupSet(BackupDescriptor backupSet, UpgradeExecutionContext context)
        {
            try
            {
                SqliteConnection.ClearAllPools();
                var destinations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["PasswordDatabase"] = context.PasswordDatabasePath,
                    ["PasswordDatabaseWal"] = context.PasswordDatabasePath + "-wal",
                    ["PasswordDatabaseShm"] = context.PasswordDatabasePath + "-shm",
                    ["PasswordDatabaseJournal"] = context.PasswordDatabasePath + "-journal",
                    ["KeyFileDatabase"] = context.KeyFilePath
                };
                BackupRestoreResult restored = _backupService.RestoreAsync(new BackupRestoreRequest
                {
                    BackupFolder = backupSet.BackupFolder,
                    ExpectedBackupType = BackupTypes.Upgrade,
                    Destinations = destinations,
                    RemoveTargetsForAbsentOptionalFiles = true
                }).GetAwaiter().GetResult();
                return restored.Succeeded
                    ? RollbackResult.Succeeded(restored.SafeMessage)
                    : RollbackResult.Failed(restored.SafeMessage);
            }
            catch (Exception ex)
            {
                return RollbackResult.Failed("Full backup set restore failed.", ex);
            }
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

        private static string GetDefaultCodeBackupPath() =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "MWPV_Rollback",
                "code");
    }
}
