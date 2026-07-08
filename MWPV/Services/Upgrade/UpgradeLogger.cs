using MWPV.Services.AppLifecycle;
using System;
using System.Linq;

namespace MWPV.Services.Upgrade
{
    public sealed class UpgradeLogger
    {
        private readonly string? _logPath;
        public string LogPath => _logPath ?? string.Empty;

        public UpgradeLogger(string? logPath = null)
        {
            _logPath = logPath;
        }

        public void LogPhase(string phase, string message)
        {
            WriteLine($"phase={phase} message={message}");
        }

        public void LogResult(AppExitCode code, string message)
        {
            WriteLine($"result={(int)code} code={code} message={message}");
        }

        public void LogManualRecoveryInstructions(
            UpgradeExecutionContext? context,
            AppExitCode finalExitCode,
            AppExitCode? detailCode,
            UpgradeFailureCategory failureCategory,
            string failureMessage,
            UpgradeBackupSet? backupSet,
            RollbackResult rollback)
        {
            WriteRawLine("");
            WriteRawLine("============================================================");
            WriteRawLine("UPGRADE FAILED - MANUAL RECOVERY INSTRUCTIONS");
            WriteRawLine("============================================================");
            WriteRawLine($"Final exit code: {(int)finalExitCode} ({finalExitCode})");
            WriteRawLine($"Failure category: {failureCategory}");
            WriteRawLine($"Detail code: {FormatCode(detailCode)}");
            WriteRawLine($"Upgrade log location: {ValueOrUnavailable(_logPath)}");
            WriteRawLine($"Backup set location: {ValueOrUnavailable(backupSet?.BackupRoot ?? context?.BackupRoot)}");
            WriteRawLine($"Failure detail: {ValueOrUnavailable(failureMessage)}");
            WriteRawLine("");
            WriteRawLine("Automatic rollback results:");
            WriteRawLine($"Status: {rollback.Status}");
            WriteRawLine($"Message: {ValueOrUnavailable(rollback.Message)}");
            if (rollback.Exception != null)
                WriteRawLine($"Exception: {rollback.Exception}");
            WriteRawLine("");

            LogDatabaseRecovery(context, backupSet, failureMessage);
            LogKeyFileRecovery(context, backupSet, failureMessage);
            LogCodeRecovery(context, failureMessage);

            WriteRawLine("Important:");
            WriteRawLine("Review the automatic rollback result above before copying files manually.");
            WriteRawLine("If automatic rollback succeeded, manual file restoration may not be necessary.");
            WriteRawLine("If automatic rollback failed or the application still does not start, restore the files listed above from the backup source locations to the restore target locations.");
            WriteRawLine("============================================================");
            WriteRawLine("");
        }

        private void LogDatabaseRecovery(UpgradeExecutionContext? context, UpgradeBackupSet? backupSet, string failureMessage)
        {
            WriteRawLine("1. Database restoration");
            WriteRawLine($"What failed: {ValueOrUnavailable(failureMessage)}");
            WriteRawLine($"Restore target location: {ValueOrUnavailable(context?.PasswordDatabasePath)}");
            WriteRawLine("Backup source location:");

            var databaseEntries = backupSet?.Files
                .Where(file => file.Role.StartsWith("PasswordDatabase", StringComparison.OrdinalIgnoreCase))
                .ToArray() ?? Array.Empty<BackupFileEntry>();

            if (databaseEntries.Length == 0)
            {
                WriteRawLine($"- {ValueOrUnavailable(backupSet?.BackupRoot ?? context?.BackupRoot)}");
            }
            else
            {
                foreach (var entry in databaseEntries)
                    WriteRawLine($"- {entry.Role}: {FormatBackupEntry(entry)}");
            }

            WriteRawLine("Recommended restore action: Copy each present password database backup file to its original target path. Include sidecar files such as -wal, -shm, or -journal when they are listed as present. Remove sidecar files that are listed as not present in the backup set.");
            WriteRawLine("");
        }

        private void LogKeyFileRecovery(UpgradeExecutionContext? context, UpgradeBackupSet? backupSet, string failureMessage)
        {
            WriteRawLine("2. .pv key-file restoration");
            WriteRawLine($"What failed: {ValueOrUnavailable(failureMessage)}");
            WriteRawLine($"Restore target location: {ValueOrUnavailable(context?.KeyFilePath)}");

            var keyEntry = backupSet?.Files
                .FirstOrDefault(file => string.Equals(file.Role, "KeyFileDatabase", StringComparison.OrdinalIgnoreCase));

            WriteRawLine($"Backup source location: {FormatBackupEntry(keyEntry)}");
            WriteRawLine("Recommended restore action: Copy the backed-up .pv key file to the restore target location and overwrite the upgraded or partially upgraded key file.");
            WriteRawLine("");
        }

        private void LogCodeRecovery(UpgradeExecutionContext? context, string failureMessage)
        {
            WriteRawLine("3. Application/code restoration");
            WriteRawLine($"What failed: {ValueOrUnavailable(failureMessage)}");
            WriteRawLine($"Backup source location: {ValueOrUnavailable(context?.CodeBackupPath)}");
            WriteRawLine($"Restore target location: {ValueOrUnavailable(context?.AppInstallDirectory)}");
            WriteRawLine("Recommended restore action: Copy the full contents of the code backup folder into the application install folder and overwrite upgraded or partially upgraded files. This restores the installer-created application/code backup.");
            WriteRawLine("");
        }

        private void WriteLine(string message)
        {
            WriteRawLine($"{System.DateTimeOffset.UtcNow:O} {message}");
        }

        private void WriteRawLine(string message)
        {
            if (string.IsNullOrWhiteSpace(_logPath))
                return;

            try
            {
                var directory = System.IO.Path.GetDirectoryName(_logPath);
                if (!string.IsNullOrWhiteSpace(directory))
                    System.IO.Directory.CreateDirectory(directory);

                System.IO.File.AppendAllText(
                    _logPath,
                    $"{message}{System.Environment.NewLine}");
            }
            catch
            {
                // Upgrade logging is best-effort and must never affect vault recovery.
            }
        }

        private static string FormatCode(AppExitCode? code) =>
            code.HasValue ? $"{(int)code.Value} ({code.Value})" : "Unavailable";

        private static string FormatBackupEntry(BackupFileEntry? entry)
        {
            if (entry == null)
                return "Unavailable";

            if (!entry.WasPresent)
                return $"Not present in backup set; original target was {ValueOrUnavailable(entry.OriginalPath)}";

            return $"{ValueOrUnavailable(entry.BackupPath)} -> {ValueOrUnavailable(entry.OriginalPath)}";
        }

        private static string ValueOrUnavailable(string? value) =>
            string.IsNullOrWhiteSpace(value) ? "Unavailable" : value.Trim();
    }
}
