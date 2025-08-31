// Utilities/Services/ExportService.cs
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Utilities.Helpers;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace Utilities.Services
{
    /// <summary>
    /// Export helpers:
    ///  - ExportEncryptedDbAsync: flush WAL and copy the encrypted DB file (no decryption)
    ///  - ExportKeyArchiveAsync: copy key archive (e.g., key.7z)
    ///  - ExportSchemaSqlAsync: dump structure-only DDL to a .sql file (no data)
    /// </summary>
    public static class ExportService
    {
        /// <summary>
        /// Exports the encrypted SQLite database by:
        /// 1) forcing a FULL WAL checkpoint (flushes -wal into main db),
        /// 2) performing a safe file copy of the main DB.
        /// No decryption occurs; the copy stays encrypted.
        /// </summary>
        public static async Task<bool> ExportEncryptedDbAsync(
            string dbPath,
            Func<SqliteConnection> openAppConnection, // returns an OPEN connection to the live DB
            string? defaultFileName = "MWPV_EncryptedBackup.db",
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(dbPath) || !File.Exists(dbPath))
            {
                ErrorHandler.InfoTitled("Export", "Database file not found.\n\n(This error has been logged.)", "ExportService.DBMissing");
                return false;
            }

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
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

                // 1) FULL checkpoint so main db is self-contained
                using (var cn = openAppConnection())
                {
                    using var cmd = cn.CreateCommand();
                    cmd.CommandText = "PRAGMA wal_checkpoint(FULL); PRAGMA optimize;";
                    await cmd.ExecuteNonQueryAsync(ct);
                }

                // 2) Copy encrypted db file as-is
                await CopyFileWithRetryAsync(dbPath, dest, overwrite: true, ct);

                ErrorHandler.InfoTitled("Export", "Encrypted database exported successfully.", "ExportService.DB");
                return true;
            }
            catch (OperationCanceledException)
            {
                ErrorHandler.InfoTitled("Export", "Export canceled.", "ExportService.DB");
                return false;
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Failed to export encrypted database", "ExportService.DB");
                return false;
            }
        }

        /// <summary>
        /// Export the key archive (e.g., key.7z) to a user-chosen file.
        /// </summary>
        public static async Task<bool> ExportKeyArchiveAsync(string keyArchivePath, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(keyArchivePath) || !File.Exists(keyArchivePath))
            {
                ErrorHandler.InfoTitled("Export", "Key archive not found.\n\n(This error has been logged.)", "ExportService.KeyMissing");
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
                Directory.CreateDirectory(Path.GetDirectoryName(sfd.FileName)!);
                await CopyFileWithRetryAsync(keyArchivePath, sfd.FileName, overwrite: true, ct);
                ErrorHandler.InfoTitled("Export", "Key archive exported successfully.", "ExportService.Key");
                return true;
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Failed to export key archive", "ExportService.Key");
                return false;
            }
        }

        /// <summary>
        /// Dumps structure-only SQL DDL (tables, views, indexes, triggers) from the live DB to a .sql file.
        /// Uses the DB's own CREATE statements from sqlite_schema; does not export data.
        /// Also captures PRAGMA user_version and application_id to preserve versioning.
        /// </summary>
        public static async Task<bool> ExportSchemaSqlAsync(
            Func<SqliteConnection> openAppConnection,   // returns an OPEN connection to the live (encrypted) DB
            string? defaultFileName = "MWPV_Schema.sql",
            CancellationToken ct = default)
        {
            var sfd = new SaveFileDialog
            {
                Title = "Export Schema (DDL only)",
                FileName = defaultFileName,
                Filter = "SQL File (*.sql)|*.sql|All Files (*.*)|*.*",
                OverwritePrompt = true
            };
            if (sfd.ShowDialog() != true) return false;

            string dest = sfd.FileName;

            try
            {
                ct.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

                using var cn = openAppConnection();
                // Read versioning pragmas
                long userVersion = await ExecScalarLongAsync(cn, "PRAGMA user_version;", ct);
                long appId = await ExecScalarLongAsync(cn, "PRAGMA application_id;", ct);

                // Pull schema objects (skip internal sqlite_% tables except sqlite_sequence if you want to keep AUTOINCREMENT behavior)
                var ddlList = new List<(string type, string name, string sql)>();

                using (var cmd = cn.CreateCommand())
                {
                    cmd.CommandText = @"
SELECT type, name, sql
FROM sqlite_schema
WHERE sql IS NOT NULL
  AND type IN ('table', 'view', 'index', 'trigger')
  AND name NOT LIKE 'sqlite_%'
ORDER BY CASE type
    WHEN 'table'  THEN 0
    WHEN 'view'   THEN 1
    WHEN 'index'  THEN 2
    WHEN 'trigger'THEN 3
    ELSE 4 END, name;";
                    using var rdr = await cmd.ExecuteReaderAsync(ct);
                    while (await rdr.ReadAsync(ct))
                    {
                        var type = rdr.GetString(0);
                        var name = rdr.GetString(1);
                        var sql = rdr.GetString(2);
                        ddlList.Add((type, name, sql));
                    }
                }

                // Write out a clean, idempotent DDL file
                var sb = new StringBuilder();
                sb.AppendLine($"-- MWPV schema export");
                sb.AppendLine($"-- Generated: {DateTime.UtcNow:O} (UTC)");
                sb.AppendLine($"-- user_version={userVersion}, application_id={appId}");
                sb.AppendLine();
                sb.AppendLine("PRAGMA foreign_keys=OFF;");
                sb.AppendLine("BEGIN;");
                sb.AppendLine();

                // Tables first (in order), then views, indexes, triggers (already ordered above)
                foreach (var (type, name, sql) in ddlList)
                {
                    // Ensure trailing semicolon
                    var stmt = sql.TrimEnd();
                    if (!stmt.EndsWith(";")) stmt += ";";
                    sb.AppendLine(stmt);
                    sb.AppendLine();
                }

                // Restore pragmas and finish
                sb.AppendLine($"PRAGMA user_version = {userVersion};");
                sb.AppendLine($"PRAGMA application_id = {appId};");
                sb.AppendLine("COMMIT;");
                sb.AppendLine("PRAGMA foreign_keys=ON;");

                await File.WriteAllTextAsync(dest, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);

                ErrorHandler.InfoTitled("Export", "Schema (DDL) exported successfully.", "ExportService.Schema");
                return true;
            }
            catch (OperationCanceledException)
            {
                ErrorHandler.InfoTitled("Export", "Schema export canceled.", "ExportService.Schema");
                return false;
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Failed to export schema (DDL)", "ExportService.Schema");
                return false;
            }
        }

        // ----------------------
        // Internals / utilities
        // ----------------------

        private static async Task<long> ExecScalarLongAsync(SqliteConnection cn, string sql, CancellationToken ct)
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandText = sql;
            var obj = await cmd.ExecuteScalarAsync(ct);
            if (obj is long l) return l;
            if (obj is int i) return i;
            if (obj is string s && long.TryParse(s, out var p)) return p;
            return 0;
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
