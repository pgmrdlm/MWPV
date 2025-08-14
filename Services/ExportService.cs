// Utilities/Services/ExportService.cs
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Utilities.Helpers;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace Utilities.Services
{
    public static class ExportService
    {
        /// <summary>
        /// Exports the encrypted SQLite database by:
        /// 1) forcing a FULL WAL checkpoint (flushes -wal into main db),
        /// 2) performing a safe file copy of the main DB.
        /// No decryption occurs; the copy stays encrypted.
        /// 
        /// All user prompts and notifications route through ErrorHandler.* for consistency & logging.
        /// </summary>
        public static async Task<bool> ExportEncryptedDbAsync(
            string dbPath,
            Func<SqliteConnection> openAppConnection, // returns an open connection to the live DB
            string? defaultFileName = "MWPV_EncryptedBackup.db",
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(dbPath) || !File.Exists(dbPath))
            {
                ErrorHandler.InfoTitled(
                    title: "Export",
                    message: "Database file not found.\n\n(This error has been logged.)",
                    stage: "ExportService.DBMissing"
                );
                return false;
            }

            // 1) Ask user for a destination
            var sfd = new SaveFileDialog
            {
                Title = "Export Encrypted Database",
                FileName = defaultFileName,
                Filter = "SQLite Database (*.db)|*.db|All Files (*.*)|*.*",
                OverwritePrompt = true
            };
            if (sfd.ShowDialog() != true) return false;

            string dest = sfd.FileName;

            try
            {
                ct.ThrowIfCancellationRequested();

                // 2) Force a FULL WAL checkpoint so the main db file is self-contained
                using (var cn = openAppConnection())
                {
                    using var cmd = cn.CreateCommand();
                    cmd.CommandText = "PRAGMA wal_checkpoint(FULL);";
                    await cmd.ExecuteNonQueryAsync(ct);
                }

                // 3) Copy the main DB (preserves encryption because we are not touching contents)
                await CopyFileWithRetryAsync(dbPath, dest, overwrite: true, ct);

                // 4) Notify success via centralized helper
                ErrorHandler.InfoTitled("Export", "Encrypted database exported successfully.", stage: "ExportService.DB");
                return true;
            }
            catch (OperationCanceledException)
            {
                ErrorHandler.InfoTitled("Export", "Export canceled.", stage: "ExportService.DB");
                return false;
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Failed to export encrypted database", stage: "ExportService.DB");
                return false;
            }
        }

        /// <summary>
        /// (Optional) Export the key archive alongside the DB.
        /// </summary>
        public static async Task<bool> ExportKeyArchiveAsync(string keyArchivePath, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(keyArchivePath) || !File.Exists(keyArchivePath))
            {
                ErrorHandler.InfoTitled(
                    title: "Export",
                    message: "Key archive not found.\n\n(This error has been logged.)",
                    stage: "ExportService.KeyMissing"
                );
                return false;
            }

            var sfd = new SaveFileDialog
            {
                Title = "Export Key Archive",
                FileName = Path.GetFileName(keyArchivePath),
                Filter = "7-Zip Archive (*.7z)|*.7z|All Files (*.*)|*.*",
                OverwritePrompt = true
            };
            if (sfd.ShowDialog() != true) return false;

            try
            {
                await CopyFileWithRetryAsync(keyArchivePath, sfd.FileName, overwrite: true, ct);
                ErrorHandler.InfoTitled("Export", "Key archive exported successfully.", stage: "ExportService.Key");
                return true;
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Failed to export key archive", stage: "ExportService.Key");
                return false;
            }
        }

        private static async Task CopyFileWithRetryAsync(string src, string dest, bool overwrite, CancellationToken ct)
        {
            const int maxAttempts = 5;
            const int delayMs = 120; // brief backoff

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var srcStream = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var dstStream = new FileStream(dest, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    await srcStream.CopyToAsync(dstStream, 1024 * 128, ct);
                    await dstStream.FlushAsync(ct);
                    return;
                }
                catch (IOException) when (attempt < maxAttempts)
                {
                    await Task.Delay(delayMs * attempt, ct);
                }
            }

            // Final attempt throws if it fails
            using var s = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var d = new FileStream(dest, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await s.CopyToAsync(d, 1024 * 128, ct);
            await d.FlushAsync(ct);
        }
    }
}
