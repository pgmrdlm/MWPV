#if DEBUG
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Utilities.Helpers; // ErrorHandler

namespace Utilities.Services
{
    public static class FullSqlExportService
    {
        public static async Task<bool> ExportFullDbAsSqlAsync(
            Func<SqliteConnection> openAppConnection,
            bool decryptLogsPayload = true,
            CancellationToken ct = default)
        {
            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export Full Database as SQL (unencrypted)",
                FileName = "MWPV_FullExport.sql",
                Filter = "SQL Script (*.sql)|*.sql|All Files (*.*)|*.*",
                OverwritePrompt = true
            };
            if (sfd.ShowDialog() != true) return false;

            try
            {
                using var cn = openAppConnection(); // OPEN & keyed
                var tables = await GetUserTablesAsync(cn, ct).ConfigureAwait(false);

                await using var fs = new global::System.IO.FileStream(
                    path: sfd.FileName,
                    mode: FileMode.Create,
                    access: FileAccess.Write,
                    share: FileShare.None);

                await using var sw = new global::System.IO.StreamWriter(
                    stream: fs,
                    encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    bufferSize: 1024,
                    leaveOpen: false);

                await sw.WriteLineAsync("-- MWPV full database export (rebuild import)").ConfigureAwait(false);
                await sw.WriteLineAsync("-- Drops tables, recreates schema, then inserts data").ConfigureAwait(false);
                await sw.WriteLineAsync("PRAGMA foreign_keys=OFF;").ConfigureAwait(false);
                await sw.WriteLineAsync("BEGIN TRANSACTION;").ConfigureAwait(false);

                // 1) DROP TABLES
                foreach (var t in tables)
                {
                    await sw.WriteLineAsync($"DROP TABLE IF EXISTS {QuoteIdent(t)};").ConfigureAwait(false);
                }
                await sw.WriteLineAsync().ConfigureAwait(false);

                // 2) CREATE TABLES
                foreach (var t in tables)
                {
                    string createSql = await GetCreateTableSqlAsync(cn, t, ct).ConfigureAwait(false);

                    if (decryptLogsPayload && t.Equals("Logs", StringComparison.OrdinalIgnoreCase))
                    {
                        // Make Logs.Payload TEXT so plaintext JSON fits on import
                        createSql = CoerceLogsPayloadToText(createSql);
                    }

                    await sw.WriteLineAsync(createSql.TrimEnd(';') + ";").ConfigureAwait(false);
                }
                await sw.WriteLineAsync().ConfigureAwait(false);

                // 3) INSERT ROWS
                foreach (var t in tables)
                {
                    await DumpTableAsync(cn, t, sw, decryptLogsPayload, ct).ConfigureAwait(false);
                    await sw.WriteLineAsync().ConfigureAwait(false);
                }

                await sw.WriteLineAsync("COMMIT;").ConfigureAwait(false);
                await sw.FlushAsync().ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(
                    ex,
                    "Full SQL export failed",
                    stage: "export-sql"
                );
                return false;
            }

        }

        // ----------- schema helpers -----------
        private static async Task<List<string>> GetUserTablesAsync(SqliteConnection cn, CancellationToken ct)
        {
            var list = new List<string>();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
            using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                list.Add(rd.GetString(0));
            return list;
        }

        private static async Task<string> GetCreateTableSqlAsync(SqliteConnection cn, string table, CancellationToken ct)
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name=$t;";
            cmd.Parameters.AddWithValue("$t", table);
            var obj = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return obj?.ToString() ?? $"CREATE TABLE \"{table}\"( /* unknown schema */ )";
        }

        private static string CoerceLogsPayloadToText(string createSql)
        {
            // Force Logs.Payload column to TEXT so JSON exports plaintext
            return Regex.Replace(createSql,
                @"(?i)(\bPayload\b\s+)(BLOB|VARBINARY|BYTEA|\w+)",
                m => m.Groups[1].Value + "TEXT");
        }

        // ----------- data dump -----------
        private static async Task DumpTableAsync(SqliteConnection cn, string table, global::System.IO.StreamWriter sw, bool decryptLogsPayload, CancellationToken ct)
        {
            var cols = new List<string>();
            using (var cmdCols = cn.CreateCommand())
            {
                cmdCols.CommandText = $"PRAGMA table_info({QuoteIdent(table)});";
                using var rd = await cmdCols.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await rd.ReadAsync(ct).ConfigureAwait(false))
                    cols.Add(rd.GetString(1));
            }

            using var cmd = cn.CreateCommand();
            cmd.CommandText = $"SELECT * FROM {QuoteIdent(table)};";
            using var rdAll = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

            while (await rdAll.ReadAsync(ct).ConfigureAwait(false))
            {
                var vals = new string[cols.Count];
                for (int i = 0; i < cols.Count; i++)
                {
                    if (rdAll.IsDBNull(i))
                    {
                        vals[i] = "NULL";
                        continue;
                    }

                    object val = rdAll.GetValue(i);
                    string col = cols[i];

                    if (decryptLogsPayload &&
                        table.Equals("Logs", StringComparison.OrdinalIgnoreCase) &&
                        col.Equals("Payload", StringComparison.OrdinalIgnoreCase) &&
                        val is byte[] blob)
                    {
                        string json = TryDecryptToJson(blob);
                        vals[i] = SqlText(json);
                    }
                    else
                    {
                        vals[i] = ToSqlLiteral(val);
                    }
                }

                string colList = string.Join(", ", cols.ConvertAll(QuoteIdent));
                string valList = string.Join(", ", vals);
                await sw.WriteLineAsync($"INSERT INTO {QuoteIdent(table)} ({colList}) VALUES ({valList});").ConfigureAwait(false);
            }
        }

        // ----------- value formatting -----------
        private static string QuoteIdent(string name) => $"\"{name.Replace("\"", "\"\"")}\"";
        private static string SqlText(string s) => $"'{s.Replace("'", "''")}'";

        private static string ToSqlLiteral(object val)
        {
            switch (val)
            {
                case DBNull: return "NULL";
                case null: return "NULL";
                case string s: return SqlText(s);
                case long l: return l.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case int i: return i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case short sh: return sh.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case bool b: return b ? "1" : "0";
                case double d: return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case float f: return f.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case decimal m: return m.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case DateTime dt: return SqlText(dt.ToString("o"));
                case byte[] bytes: return "X'" + BitConverter.ToString(bytes).Replace("-", "") + "'";
                default: return SqlText(Convert.ToString(val, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
            }
        }

        // ----------- decrypt Logs.Payload -----------
        private static string TryDecryptToJson(byte[] blob)
        {
            try
            {
                var key = DeriveKey32();

                if (blob.Length < 12 + 16)
                    return JsonSerializer.Serialize(new { decryptError = true, reason = "blobTooSmall" });

                var iv = new ReadOnlySpan<byte>(blob, 0, 12).ToArray();
                var tag = new ReadOnlySpan<byte>(blob, blob.Length - 16, 16).ToArray();
                var ct = new ReadOnlySpan<byte>(blob, 12, blob.Length - 12 - 16).ToArray();

                var pt = new byte[ct.Length];
                using var aes = new AesGcm(key);
                aes.Decrypt(iv, ct, tag, pt);
                return Encoding.UTF8.GetString(pt);
            }
            catch
            {
                return JsonSerializer.Serialize(new { decryptError = true });
            }
        }

        private static byte[] DeriveKey32()
        {
            // Keep in sync with MWPV.Services.LogRepository.MachineKeyProvider
            string machineGuid = Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", "")?.ToString() ?? "";

            string driveSerial = GetSystemDriveSerial() ?? "";
            string material = driveSerial + "|" + machineGuid;

            using var kdf = new Rfc2898DeriveBytes(
                password: Encoding.UTF8.GetBytes(material),
                salt: Encoding.UTF8.GetBytes("b1b9a2d6-5d6a-4c5b-9c24-1f2b7a8a8f13"),
                iterations: 100_000,
                hashAlgorithm: HashAlgorithmName.SHA256);

            return kdf.GetBytes(32);
        }

        // Same P/Invoke used in LogRepository
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetVolumeInformationW(
            string lpRootPathName,
            StringBuilder? lpVolumeNameBuffer,
            uint nVolumeNameSize,
            out uint lpVolumeSerialNumber,
            out uint lpMaximumComponentLength,
            out uint lpFileSystemFlags,
            StringBuilder? lpFileSystemNameBuffer,
            uint nFileSystemNameSize);

        private static string? GetSystemDriveSerial()
        {
            try
            {
                uint serial, maxCompLen, fsFlags;
                bool ok = GetVolumeInformationW(@"C:\", null, 0, out serial, out maxCompLen, out fsFlags, null, 0);
                if (!ok) return null;
                return serial.ToString("X8");
            }
            catch { return null; }
        }
    }
}
#endif
