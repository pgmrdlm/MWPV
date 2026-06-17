using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MWPV.Services.AppLifecycle;

namespace MWPV.Services.Upgrade
{
    public sealed class VaultDataBackupService
    {
        private const string RolePasswordDatabase = "PasswordDatabase";
        private const string RolePasswordDatabaseWal = "PasswordDatabaseWal";
        private const string RolePasswordDatabaseShm = "PasswordDatabaseShm";
        private const string RolePasswordDatabaseJournal = "PasswordDatabaseJournal";
        private const string RoleKeyFileDatabase = "KeyFileDatabase";

        private static readonly JsonSerializerOptions ManifestJsonOptions = new()
        {
            WriteIndented = true
        };

        public OperationResult<UpgradeBackupSet> CreateBackupSet(
            string passwordDatabasePath,
            string keyFileDatabasePath,
            string backupRoot)
        {
            try
            {
                ValidateRequiredPath(passwordDatabasePath, nameof(passwordDatabasePath));
                ValidateRequiredPath(keyFileDatabasePath, nameof(keyFileDatabasePath));
                ValidateRequiredPath(backupRoot, nameof(backupRoot));

                if (!File.Exists(passwordDatabasePath))
                {
                    return OperationResult<UpgradeBackupSet>.Failure(
                        AppExitCode.UpgradeBackupSetCreationFailed,
                        $"Required password database was not found: {passwordDatabasePath}");
                }

                if (!File.Exists(keyFileDatabasePath))
                {
                    return OperationResult<UpgradeBackupSet>.Failure(
                        AppExitCode.UpgradeBackupSetCreationFailed,
                        $"Required key-file database was not found: {keyFileDatabasePath}");
                }

                var backupSetId = Guid.NewGuid().ToString("D");
                var setRoot = Path.Combine(Path.GetFullPath(backupRoot), backupSetId);
                var filesRoot = Path.Combine(setRoot, "files");
                Directory.CreateDirectory(filesRoot);

                var entries = new List<BackupFileEntry>
                {
                    CopyEntry(RolePasswordDatabase, passwordDatabasePath, filesRoot, required: true),
                    CopyEntry(RolePasswordDatabaseWal, passwordDatabasePath + "-wal", filesRoot, required: false),
                    CopyEntry(RolePasswordDatabaseShm, passwordDatabasePath + "-shm", filesRoot, required: false),
                    CopyEntry(RolePasswordDatabaseJournal, passwordDatabasePath + "-journal", filesRoot, required: false),
                    CopyEntry(RoleKeyFileDatabase, keyFileDatabasePath, filesRoot, required: true)
                };

                var backupSet = new UpgradeBackupSet
                {
                    BackupSetId = backupSetId,
                    BackupRoot = setRoot,
                    ManifestPath = Path.Combine(setRoot, "manifest.json"),
                    CreatedUtc = DateTimeOffset.UtcNow,
                    AppVersion = Assembly.GetEntryAssembly()?.GetName()?.Version?.ToString() ?? string.Empty,
                    Files = entries
                };

                WriteManifest(backupSet);

                var verify = VerifyBackupSet(backupSet);
                if (!verify.Succeeded)
                {
                    return OperationResult<UpgradeBackupSet>.Failure(
                        verify.Code,
                        verify.Message,
                        verify.Exception);
                }

                return OperationResult<UpgradeBackupSet>.Success(
                    backupSet,
                    $"Backup set created: {backupSet.BackupSetId}");
            }
            catch (Exception ex)
            {
                return OperationResult<UpgradeBackupSet>.Failure(
                    AppExitCode.UpgradeBackupSetCreationFailed,
                    "Backup set creation failed.",
                    ex);
            }
        }

        public OperationResult CreateBackupSetPlaceholder() =>
            OperationResult.Failure(
                AppExitCode.UpgradeBackupSetCreationFailed,
                "Use CreateBackupSet(passwordDatabasePath, keyFileDatabasePath, backupRoot).");

        public OperationResult VerifyBackupSet(UpgradeBackupSet backupSet)
        {
            try
            {
                ArgumentNullException.ThrowIfNull(backupSet);

                if (string.IsNullOrWhiteSpace(backupSet.BackupRoot) ||
                    !Directory.Exists(backupSet.BackupRoot))
                {
                    return OperationResult.Failure(
                        AppExitCode.UpgradeBackupSetCreationFailed,
                        "Backup root is missing.");
                }

                if (string.IsNullOrWhiteSpace(backupSet.ManifestPath) ||
                    !File.Exists(backupSet.ManifestPath))
                {
                    return OperationResult.Failure(
                        AppExitCode.UpgradeBackupSetCreationFailed,
                        "Backup manifest is missing.");
                }

                foreach (var entry in backupSet.Files)
                {
                    if (entry.Required && !entry.WasPresent)
                    {
                        return OperationResult.Failure(
                            AppExitCode.UpgradeBackupSetCreationFailed,
                            $"Required backup entry was not present: {entry.Role}");
                    }

                    if (!entry.WasPresent)
                        continue;

                    if (string.IsNullOrWhiteSpace(entry.BackupPath) ||
                        !File.Exists(entry.BackupPath))
                    {
                        return OperationResult.Failure(
                            AppExitCode.UpgradeBackupSetCreationFailed,
                            $"Backup file is missing for role {entry.Role}.");
                    }

                    var info = new FileInfo(entry.BackupPath);
                    if (entry.Size != null && info.Length != entry.Size.Value)
                    {
                        return OperationResult.Failure(
                            AppExitCode.UpgradeBackupSetCreationFailed,
                            $"Backup file size mismatch for role {entry.Role}.");
                    }

                    if (!string.IsNullOrWhiteSpace(entry.Sha256))
                    {
                        var actualHash = ComputeSha256(entry.BackupPath);
                        if (!string.Equals(actualHash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                        {
                            return OperationResult.Failure(
                                AppExitCode.UpgradeBackupSetCreationFailed,
                                $"Backup file hash mismatch for role {entry.Role}.");
                        }
                    }
                }

                return OperationResult.Success("Backup set verified.");
            }
            catch (Exception ex)
            {
                return OperationResult.Failure(
                    AppExitCode.UpgradeBackupSetCreationFailed,
                    "Backup set verification failed.",
                    ex);
            }
        }

        public RollbackResult RestoreBackupSet(UpgradeBackupSet backupSet)
        {
            try
            {
                ArgumentNullException.ThrowIfNull(backupSet);
                SqliteConnection.ClearAllPools();

                var verify = VerifyBackupSet(backupSet);
                if (!verify.Succeeded)
                    return RollbackResult.Failed(verify.Message, verify.Exception);

                RemoveAbsentPasswordDatabaseSidecars(backupSet);

                foreach (var entry in backupSet.Files.Where(file => file.WasPresent))
                {
                    if (string.IsNullOrWhiteSpace(entry.BackupPath) ||
                        !File.Exists(entry.BackupPath))
                    {
                        return RollbackResult.Failed($"Backup file is missing for role {entry.Role}.");
                    }

                    EnsureDestinationDirectory(entry.OriginalPath);
                    TryClearReadOnly(entry.OriginalPath);
                    File.Copy(entry.BackupPath, entry.OriginalPath, overwrite: true);

                    if (!string.IsNullOrWhiteSpace(entry.Sha256))
                    {
                        var restoredHash = ComputeSha256(entry.OriginalPath);
                        if (!string.Equals(restoredHash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                            return RollbackResult.Failed($"Restored file hash mismatch for role {entry.Role}.");
                    }
                }

                RemoveAbsentPasswordDatabaseSidecars(backupSet);

                return RollbackResult.Succeeded("Full backup set restored.");
            }
            catch (Exception ex)
            {
                return RollbackResult.Failed("Full backup set restore failed.", ex);
            }
        }

        public OperationResult<UpgradeBackupSet> LoadManifest(string manifestPath)
        {
            try
            {
                ValidateRequiredPath(manifestPath, nameof(manifestPath));
                if (!File.Exists(manifestPath))
                {
                    return OperationResult<UpgradeBackupSet>.Failure(
                        AppExitCode.UpgradeBackupSetCreationFailed,
                        $"Backup manifest was not found: {manifestPath}");
                }

                var json = File.ReadAllText(manifestPath);
                var backupSet = JsonSerializer.Deserialize<UpgradeBackupSet>(json, ManifestJsonOptions);
                if (backupSet == null)
                {
                    return OperationResult<UpgradeBackupSet>.Failure(
                        AppExitCode.UpgradeBackupSetCreationFailed,
                        "Backup manifest could not be parsed.");
                }

                return OperationResult<UpgradeBackupSet>.Success(backupSet, "Backup manifest loaded.");
            }
            catch (Exception ex)
            {
                return OperationResult<UpgradeBackupSet>.Failure(
                    AppExitCode.UpgradeBackupSetCreationFailed,
                    "Backup manifest load failed.",
                    ex);
            }
        }

        private static BackupFileEntry CopyEntry(
            string role,
            string originalPath,
            string filesRoot,
            bool required)
        {
            var fullOriginalPath = Path.GetFullPath(originalPath);
            var exists = File.Exists(fullOriginalPath);

            if (!exists)
            {
                if (required)
                    throw new FileNotFoundException($"Required file was not found for role {role}.", fullOriginalPath);

                return new BackupFileEntry
                {
                    Role = role,
                    OriginalPath = fullOriginalPath,
                    Required = false,
                    WasPresent = false
                };
            }

            var backupPath = Path.Combine(filesRoot, BuildBackupFileName(role, fullOriginalPath));
            File.Copy(fullOriginalPath, backupPath, overwrite: false);

            var info = new FileInfo(backupPath);
            return new BackupFileEntry
            {
                Role = role,
                OriginalPath = fullOriginalPath,
                BackupPath = backupPath,
                Required = required,
                WasPresent = true,
                Size = info.Length,
                Sha256 = ComputeSha256(backupPath)
            };
        }

        private static string BuildBackupFileName(string role, string originalPath)
        {
            var extension = Path.GetExtension(originalPath);
            if (string.IsNullOrWhiteSpace(extension) &&
                (originalPath.EndsWith("-wal", StringComparison.OrdinalIgnoreCase) ||
                 originalPath.EndsWith("-shm", StringComparison.OrdinalIgnoreCase) ||
                 originalPath.EndsWith("-journal", StringComparison.OrdinalIgnoreCase)))
            {
                extension = ".sqlite-sidecar";
            }

            return role + extension;
        }

        private static void WriteManifest(UpgradeBackupSet backupSet)
        {
            EnsureDestinationDirectory(backupSet.ManifestPath);
            var json = JsonSerializer.Serialize(backupSet, ManifestJsonOptions);
            File.WriteAllText(backupSet.ManifestPath, json);
        }

        private static void RemoveAbsentPasswordDatabaseSidecars(UpgradeBackupSet backupSet)
        {
            foreach (var entry in backupSet.Files.Where(IsAbsentPasswordDatabaseSidecar))
            {
                TryDeleteFile(entry.OriginalPath);
            }
        }

        private static bool IsAbsentPasswordDatabaseSidecar(BackupFileEntry entry) =>
            !entry.WasPresent &&
            (string.Equals(entry.Role, RolePasswordDatabaseWal, StringComparison.Ordinal) ||
             string.Equals(entry.Role, RolePasswordDatabaseShm, StringComparison.Ordinal) ||
             string.Equals(entry.Role, RolePasswordDatabaseJournal, StringComparison.Ordinal));

        private static void TryDeleteFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            TryClearReadOnly(path);
            File.Delete(path);
        }

        private static void TryClearReadOnly(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        }

        private static void EnsureDestinationDirectory(string path)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
        }

        private static string ComputeSha256(string path)
        {
            using var stream = File.OpenRead(path);
            var hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash);
        }

        private static void ValidateRequiredPath(string path, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path is required.", parameterName);
        }
    }
}
