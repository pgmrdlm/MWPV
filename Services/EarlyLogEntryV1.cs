using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Utilities.Security;   // SecureEncryptedDataStore
using Utilities.Helpers;    // DatabaseHelper

namespace MWPV.Services
{
    public sealed class EarlyLogEntryV1
    {
        public int ver { get; set; } = 1;
        public string logGuid { get; set; }           // optional but preferred for dedupe
        public DateTime whenUtc { get; set; }
        public string level { get; set; } = "INFO";
        public string source { get; set; }
        public string eventCode { get; set; }
        public string sessionId { get; set; } = "";
        public string machineId { get; set; }
        public string appVersion { get; set; } = "";
        public bool isCrash { get; set; }
        public string payloadFmt { get; set; } = "json";
        public object payload { get; set; }           // object or string; we serialize to string
    }

    public static class EarlyLogIngestor
    {
        // %LOCALAPPDATA%\MWPV\early
        public static string EarlyDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MWPV", "early");

        public static string QuarantineDir => Path.Combine(EarlyDir, "quarantine");

        static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        public static async Task<(int read, int inserted, int dupes, int quarantined)> IngestAsync(int maxBatch = 200)
        {
            Directory.CreateDirectory(EarlyDir);
            Directory.CreateDirectory(QuarantineDir);

            // Gather up to N files, oldest first
            var files = new DirectoryInfo(EarlyDir)
                .EnumerateFiles("*.elog", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f.CreationTimeUtc)
                .Take(Math.Max(1, maxBatch))
                .ToList();

            if (files.Count == 0) return (0, 0, 0, 0);

            var insertSql = SecureEncryptedDataStore.GetString("Logs_Insert_V2.sql");
            var lastIdSql = SecureEncryptedDataStore.HasKey("Logs_LastInsertId.sql")
                ? SecureEncryptedDataStore.GetString("Logs_LastInsertId.sql")
                : "SELECT last_insert_rowid();";

            int read = 0, inserted = 0, dupes = 0, quarantined = 0;

            using var conn = DatabaseHelper.OpenConnection();
            using var tx = conn.BeginTransaction();

            foreach (var fi in files)
            {
                read++;
                EarlyLogEntryV1 entry;
                try
                {
                    using var fs = fi.OpenRead();
                    entry = JsonSerializer.Deserialize<EarlyLogEntryV1>(fs, JsonOpts);
                    if (entry == null || entry.ver != 1)
                        throw new InvalidDataException("Unsupported or null entry.");
                }
                catch
                {
                    Quarantine(fi, ref quarantined);
                    continue;
                }

                string payloadText = entry.payload is string s
                    ? s
                    : JsonSerializer.Serialize(entry.payload ?? new { }, JsonOpts);

                string dedupeKey = !string.IsNullOrWhiteSpace(entry.logGuid)
                    ? $"guid:{entry.logGuid}"
                    : "sha256:" + Sha256($"{entry.whenUtc:o}|{entry.level}|{entry.source}|{entry.eventCode}|{payloadText}");

                // If duplicate exists, skip (dupe counted) + delete source file
                if (ExistsByStackHash(conn, tx, dedupeKey))
                {
                    dupes++;
                    SafeDelete(fi);
                    continue;
                }

                try
                {
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = insertSql;

                    // Parameters expected by Logs_Insert_V2.sql
                    cmd.Parameters.AddWithValue("@WhenUtc", entry.whenUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"));
                    cmd.Parameters.AddWithValue("@CreatedUtc", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
                    cmd.Parameters.AddWithValue("@Level", entry.level ?? "INFO");
                    cmd.Parameters.AddWithValue("@Source", entry.source ?? "");
                    cmd.Parameters.AddWithValue("@EventCode", entry.eventCode ?? "");
                    cmd.Parameters.AddWithValue("@SessionId", entry.sessionId ?? "");
                    cmd.Parameters.AddWithValue("@MachineId", entry.machineId ?? "");
                    cmd.Parameters.AddWithValue("@AppVersion", entry.appVersion ?? "");
                    cmd.Parameters.AddWithValue("@IsCrash", entry.isCrash ? 1 : 0);
                    cmd.Parameters.AddWithValue("@Payload", payloadText ?? "");
                    cmd.Parameters.AddWithValue("@PayloadFmt", entry.payloadFmt ?? "json");
                    cmd.Parameters.AddWithValue("@StackHash", dedupeKey);

                    var rc = await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                    if (rc > 0)
                    {
                        inserted++;
                        SafeDelete(fi);
                    }
                    else
                    {
                        Quarantine(fi, ref quarantined);
                    }
                }
                catch
                {
                    Quarantine(fi, ref quarantined);
                }
            }

            tx.Commit();
            return (read, inserted, dupes, quarantined);
        }

        private static bool ExistsByStackHash(SqliteConnection conn, SqliteTransaction tx, string key)
        {
            using var check = conn.CreateCommand();
            check.Transaction = tx;
            check.CommandText = "SELECT 1 FROM Logs WHERE StackHash = @h LIMIT 1;";
            check.Parameters.AddWithValue("@h", key);
            var o = check.ExecuteScalar();
            return o != null && o != DBNull.Value;
        }

        private static string Sha256(string s)
        {
            using var sha = SHA256.Create();
            var b = sha.ComputeHash(Encoding.UTF8.GetBytes(s ?? ""));
            var sb = new StringBuilder(b.Length * 2);
            foreach (var by in b) sb.Append(by.ToString("x2"));
            return sb.ToString();
        }

        private static void SafeDelete(FileInfo fi)
        {
            try { fi.IsReadOnly = false; fi.Delete(); } catch { /* best effort */ }
        }

        private static void Quarantine(FileInfo fi, ref int count)
        {
            try
            {
                var dest = Path.Combine(QuarantineDir, fi.Name);
                if (File.Exists(dest)) File.Delete(dest);
                fi.MoveTo(dest);
                count++;
            }
            catch { /* best effort */ }
        }
    }
}
