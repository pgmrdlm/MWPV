using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Backup.Utility;
using Microsoft.Data.Sqlite;
using Security.Utility.Storage;
using Utilities.Helpers;

namespace MWPV.Services
{
    public sealed class BackupOnExitCoordinator
    {
        private const string SedsKeyKeyFile = "KeyFile";
        private readonly IBackupService _backupService;

        public BackupOnExitCoordinator() : this(new BackupService()) { }
        public BackupOnExitCoordinator(IBackupService backupService) =>
            _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));

        public sealed record Result(bool Succeeded, bool RetentionWarning = false);

        public async Task<Result> CreateVerifiedBackupAsync()
        {
            try
            {
                int retentionCount = AppSettingsService.GetBackupRetentionCount();
                if (!RunFullCheckpoint())
                    return new Result(false);

                SqliteConnection.ClearAllPools();
                string databasePath = DatabaseHelper.GetAppDbPath();
                string keyFilePath = SecureEncryptedDataStore.GetString(SedsKeyKeyFile);
                string dataRoot = Path.GetDirectoryName(databasePath) ?? AppContext.BaseDirectory;
                string backupRoot = Path.Combine(dataRoot, "backups");

                BackupCreateResult created = await _backupService.CreateAsync(new BackupCreateRequest
                {
                    BackupRoot = backupRoot,
                    FolderPrefix = "MWPV_Backup",
                    BackupType = BackupTypes.Exit,
                    ApplicationName = "MWPV",
                    ApplicationVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? string.Empty,
                    Files = BuildVaultFiles(databasePath, keyFilePath)
                }).ConfigureAwait(false);
                if (!created.Succeeded || created.Backup == null)
                    return new Result(false);

                BackupRetentionResult retention = await _backupService.ApplyRetentionAsync(new BackupRetentionRequest
                {
                    BackupRoot = backupRoot,
                    BackupType = BackupTypes.Exit,
                    RetainCount = Math.Max(5, retentionCount),
                    ProtectedBackupSetId = created.Backup.Manifest.BackupSetId
                }).ConfigureAwait(false);

                // Deliberate future hook: BACKUP_CREATED_ON_EXIT may be logged here.
                return new Result(true, !retention.Succeeded);
            }
            catch
            {
                return new Result(false);
            }
        }

        internal static BackupSourceFile[] BuildVaultFiles(string databasePath, string keyFilePath) =>
        [
            new() { Role = "PasswordDatabase", SourcePath = databasePath, DestinationRelativePath = "files/MWPV.db", Required = true },
            new() { Role = "PasswordDatabaseWal", SourcePath = databasePath + "-wal", DestinationRelativePath = "files/PasswordDatabase.wal", Required = false },
            new() { Role = "PasswordDatabaseShm", SourcePath = databasePath + "-shm", DestinationRelativePath = "files/PasswordDatabase.shm", Required = false },
            new() { Role = "PasswordDatabaseJournal", SourcePath = databasePath + "-journal", DestinationRelativePath = "files/PasswordDatabase.journal", Required = false },
            new() { Role = "KeyFileDatabase", SourcePath = keyFilePath, DestinationRelativePath = "files/KeyFileDatabase.pv", Required = true }
        ];

        private static bool RunFullCheckpoint()
        {
            try
            {
                using var connection = DatabaseHelper.GetAppOpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA wal_checkpoint(FULL);";
                using var reader = command.ExecuteReader();
                if (!reader.Read()) return false;
                int busy = Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture);
                int logFrames = Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture);
                int checkpointedFrames = Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture);
                return busy == 0 && checkpointedFrames >= logFrames;
            }
            catch { return false; }
        }
    }
}
