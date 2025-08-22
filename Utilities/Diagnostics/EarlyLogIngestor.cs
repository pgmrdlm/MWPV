using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Utilities.Helpers;          // DatabaseHelper
using Utilities.Security;         // SecureEncryptedDataStore, SensitiveDataCleaner

namespace Utilities.Diagnostics
{
    public static class EarlyLogIngestor
    {
        public sealed record Result(int Inserted, int Deduped, int Quarantined, int Deleted, int Errors);

        private static readonly string EarlyDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MWPV", "early");

        public static Result IngestAllEarlyLogsTransactional(Func<SqliteConnection> openConn)
        {
            Directory.CreateDirectory(EarlyDir);
            var files = Directory.GetFiles(EarlyDir, "*.elog", SearchOption.TopDirectoryOnly);

            if (files.Length == 0)
                return new Result(0, 0, 0, 0, 0);

            int inserted = 0, deduped = 0, quarantined = 0, deleted = 0, errors = 0;

            using var conn = openConn();
            using var tx = conn.BeginTransaction();

            // load SQL from the catalog (keeps the source clean)
            string insertSql = SecureEncryptedDataStore.GetString("Logs_Insert_V2.sql") ?? throw new InvalidOperationException("Logs_Insert_V2.sql missing");
            string existsSql = SecureEncryptedDataStore.GetString("Logs_Exists_BySig.sql") ?? "SELECT 1 FROM Logs WHERE StackHash=@Sig LIMIT 1;";

            using var exists = conn.CreateCommand();
            exists.Transaction = tx;
            exists.CommandText = existsSql;
            var pSig = exists.CreateParameter(); pSig.ParameterName = "@Sig"; exists.Parameters.Add(pSig);

            using var insert = conn.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = insertSql;

            foreach (var file in files)
            {
                try
                {
                    if (!TryReadEarlyLog(file, out var log))
                    {
                        // couldn’t decrypt/parse → quarantine
                        Quarantine(file);
                        quarantined++;
                        continue;
                    }

                    var sig = ComputeSignature(log);

                    // dedupe check
                    pSig.Value = sig;
                    var already = exists.ExecuteScalar() is not null;
                    if (already)
                    {
                        deduped++;
                        // safe to delete the file now
                        SensitiveDataCleaner.SecureFileDelete(file, overwritePasses: 1);
                        deleted++;
                        continue;
                    }

                    PrepareInsert(insert, log, sig);
                    var n = insert.ExecuteNonQuery();
                    if (n > 0)
                    {
                        inserted++;
                        SensitiveDataCleaner.SecureFileDelete(file, overwritePasses: 1);
                        deleted++;
                    }
                    else
                    {
                        errors++;
                    }
                }
                catch
                {
                    errors++;
                    // conservative: quarantine the file so we can inspect later
                    try { Quarantine(file); quarantined++; } catch { /* ignore */ }
                }
            }

            tx.Commit();
            return new Result(inserted, deduped, quarantined, deleted, errors);
        }

        // === helpers ===

        private static void PrepareInsert(SqliteCommand cmd, EarlyLog log, string sig)
        {
            // Clear & (re)bind – relies on parameter names used by Logs_Insert_V2.sql
            cmd.Parameters.Clear();

            void Add(string name, object? value)
            {
                var p = cmd.CreateParameter();
                p.ParameterName = name;
                p.Value = value ?? DBNull.Value;
                cmd.Parameters.Add(p);
            }

            Add("@WhenUtc", log.WhenUtc);
            Add("@CreatedUtc", DateTime.UtcNow);
            Add("@Level", log.Level);
            Add("@Source", log.Source);
            Add("@EventCode", log.EventCode);
            Add("@SessionId", log.SessionId);
            Add("@AppVersion", log.AppVersion);
            Add("@IsCrash", log.IsCrash ? 1 : 0);
            Add("@Payload", log.PayloadJson);
            Add("@PayloadFmt", "json");
            Add("@StackHash", sig);               // reuse as content signature
        }

        private static string ComputeSignature(EarlyLog log)
        {
            // stable, order-sensitive signature over core fields
            var s = $"{log.WhenUtc:O}|{log.Level}|{log.Source}|{log.EventCode}|{(log.IsCrash ? 1 : 0)}|{log.PayloadJson}";
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
            return Convert.ToHexString(bytes);    // uppercase hex
        }

        private static void Quarantine(string path)
        {
            var q = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MWPV", "quarantine");
            Directory.CreateDirectory(q);
            var dest = Path.Combine(q, Path.GetFileName(path));
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(path, dest);
        }

        // NOTE: plug your actual decrypt/parse here. For now we accept plaintext JSON fallback.
        private static bool TryReadEarlyLog(string path, out EarlyLog log)
        {
            // TODO: DPAPI retry ×3 around the decrypt step if files are protected.
            var json = File.ReadAllText(path); // replace with decrypt-if-needed
            // very light “parse” – treat as already-minified JSON payload
            log = new EarlyLog
            {
                WhenUtc = File.GetCreationTimeUtc(path),
                Level = "INFO",
                Source = "EarlyLogin",
                EventCode = "EARLY_LOGIN_FAILURE",
                SessionId = null,
                AppVersion = "unknown",
                IsCrash = false,
                PayloadJson = json
            };
            return true;
        }

        private sealed class EarlyLog
        {
            public DateTime WhenUtc { get; init; }
            public string Level { get; init; } = "INFO";
            public string Source { get; init; } = "Early";
            public string EventCode { get; init; } = "EARLY";
            public string? SessionId { get; init; }
            public string AppVersion { get; init; } = "unknown";
            public bool IsCrash { get; init; }
            public string PayloadJson { get; init; } = "{}";
        }
    }
}
