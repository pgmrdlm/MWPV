// MWPV/Services/EarlyLogEntryV1.cs
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Utilities.Sql;             // SecureSql.Require(...)
using Utilities.Security;        // SecureEncryptedDataStore

namespace MWPV.Services
{
    /// <summary>
    /// Development writer for "early" logs using the canonical Logs v2 schema.
    /// Payload is encrypted with AES-256-GCM using LogPayloadKey and stored as:
    ///     nonce(12) | ciphertext(N) | tag(16)   in the Payload (BLOB) column
    /// PayloadFmt is "gcm-json-v1".
    /// 
    /// Notes:
    /// - During development, schema/version is always treated as 1 (see DDL).
    /// - This class intentionally mirrors LogRepository’s write path so both
    ///   produce identical payloads.
    /// </summary>
    public sealed class EarlyLogEntryV1
    {
        // ----- Basic fields you likely already had on early entries -----
        public DateTime WhenUtc { get; set; } = DateTime.UtcNow;
        public string Level { get; set; } = "INFO"; // TRACE|DEBUG|INFO|WARN|ERROR|FATAL|WARNING
        public string? Source { get; set; }
        public string? EventCode { get; set; }
        public string SessionId { get; set; } = "";
        public string? MachineId { get; set; } = "";
        public string AppVersion { get; set; } = "";
        public bool IsCrash { get; set; }
        public string? StackHash { get; set; }

        /// <summary>
        /// The "clear" object to be JSON-serialized (or a raw JSON string).
        /// This is what we encrypt before inserting to the DB.
        /// </summary>
        public object? PayloadObject { get; set; }

        // -----------------------------------------------------------------
        // Single-row insert
        // -----------------------------------------------------------------
        public async Task<long> InsertAsync(SqliteConnection openConnection)
        {
            if (openConnection == null) throw new ArgumentNullException(nameof(openConnection));

            string insertSql;
            string lastIdSql;
            try
            {
                insertSql = SecureSql.Require("Logs_Insert_V2.sql");
                lastIdSql = SecureSql.Require("Logs_LastInsertId.sql");
            }
            catch
            {
                // Scripts not loaded — non-fatal in dev
                return 0L;
            }

            // Build the clear JSON
            var json = PayloadObject switch
            {
                null => "",
                string s => s,
                _ => JsonSerializer.Serialize(PayloadObject, new JsonSerializerOptions { WriteIndented = false })
            };
            var clearBytes = Encoding.UTF8.GetBytes(json);

            // Encrypt → nonce|cipher|tag blob
            var encryptedBlob = CryptoLogCodec.Encrypt(clearBytes);

            using (var cmd = openConnection.CreateCommand())
            {
                cmd.CommandText = insertSql;

                cmd.Parameters.AddWithValue("@WhenUtc", WhenUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"));
                cmd.Parameters.AddWithValue("@CreatedUtc", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
                cmd.Parameters.AddWithValue("@Level", NormalizeLevel(Level));
                cmd.Parameters.AddWithValue("@Source", Source ?? string.Empty);
                cmd.Parameters.AddWithValue("@EventCode", EventCode ?? string.Empty);
                cmd.Parameters.AddWithValue("@SessionId", SessionId ?? string.Empty);
                cmd.Parameters.AddWithValue("@MachineId", MachineId ?? string.Empty);
                cmd.Parameters.AddWithValue("@AppVersion", AppVersion ?? string.Empty);
                cmd.Parameters.AddWithValue("@IsCrash", IsCrash ? 1 : 0);

                var p = cmd.CreateParameter();
                p.ParameterName = "@Payload";
                p.SqliteType = SqliteType.Blob;
                p.Value = encryptedBlob ?? Array.Empty<byte>();
                cmd.Parameters.Add(p);

                cmd.Parameters.AddWithValue("@PayloadFmt", "gcm-json-v1");
                cmd.Parameters.AddWithValue("@StackHash", (object?)StackHash ?? DBNull.Value);

                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            using var idCmd = openConnection.CreateCommand();
            idCmd.CommandText = lastIdSql;
            var idObj = await idCmd.ExecuteScalarAsync().ConfigureAwait(false);
            return idObj is long id ? id : Convert.ToInt64(idObj ?? 0L);
        }

        // -----------------------------------------------------------------
        // Batch ingest helper: runs all inserts in one transaction.
        // -----------------------------------------------------------------
        public static async Task<int> IngestAsync(IEnumerable<EarlyLogEntryV1> entries, Func<SqliteConnection> openConnectionFactory)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            if (openConnectionFactory == null) throw new ArgumentNullException(nameof(openConnectionFactory));

            int count = 0;
            using var cn = openConnectionFactory();
            await cn.OpenAsync().ConfigureAwait(false);

            using var tx = cn.BeginTransaction();
            try
            {
                foreach (var e in entries)
                {
                    await e.InsertAsync(cn).ConfigureAwait(false);
                    count++;
                }
                tx.Commit();
            }
            catch
            {
                try { tx.Rollback(); } catch { /* ignore */ }
                throw;
            }
            return count;
        }

        private static string NormalizeLevel(string? level)
        {
            var s = (level ?? "INFO").Trim().ToUpperInvariant();
            return s switch
            {
                "TRACE" => "TRACE",
                "DEBUG" => "DEBUG",
                "INFO" => "INFO",
                "WARN" => "WARN",
                "WARNING" => "WARNING",
                "ERROR" => "ERROR",
                "FATAL" => "FATAL",
                "CRITICAL" => "FATAL",
                _ => "INFO"
            };
        }

        // === Minimal AES-GCM helper mirroring LogRepository ===
        private static class CryptoLogCodec
        {
            private static byte[] GetLogKey()
            {
                if (!SecureEncryptedDataStore.HasKey("LogPayloadKey"))
                    throw new InvalidOperationException("LogPayloadKey not loaded into SecureEncryptedDataStore.");
                var key = SecureEncryptedDataStore.GetBytes("LogPayloadKey");
                if (key == null || key.Length != 32)
                    throw new InvalidOperationException("LogPayloadKey is missing or not 32 bytes.");
                return key;
            }

            public static byte[] Encrypt(byte[] plaintext)
            {
                plaintext ??= Array.Empty<byte>();
                var key = GetLogKey();
                var iv = RandomNumberGenerator.GetBytes(12);
                var ct = new byte[plaintext.Length];
                var tag = new byte[16];

                using var gcm = new AesGcm(key);
                gcm.Encrypt(iv, plaintext, ct, tag);

                var blob = new byte[iv.Length + ct.Length + tag.Length];
                Buffer.BlockCopy(iv, 0, blob, 0, iv.Length);
                Buffer.BlockCopy(ct, 0, blob, iv.Length, ct.Length);
                Buffer.BlockCopy(tag, 0, blob, iv.Length + ct.Length, tag.Length);
                return blob;
            }
        }
    }
}
