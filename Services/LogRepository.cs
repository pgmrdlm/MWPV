// MWPV/Services/LogRepository.cs
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Utilities.Sql;             // SecureSql
using Utilities.Security;        // SecureEncryptedDataStore

namespace MWPV.Services
{
    public enum LogLevel { Trace, Info, Warn, Error, Critical }

    /// <summary>
    /// Writes encrypted rows into Logs (v2 schema).
    /// Dev payload format: AES-256-GCM, raw bytes stored in Payload as:
    ///     nonce(12) | ciphertext(N) | tag(16)
    /// PayloadFmt: "gcm-json-v1"
    /// NOTE: During development, schema/version is always treated as 1 (see DDL).
    /// </summary>
    public sealed class LogRepository
    {
        private const string SqlInsertKey = "Logs_Insert_V2.sql";
        private const string SqlSelectRecentKey = "Logs_Select_Recent.sql";
        private const string SqlLastInsertIdKey = "Logs_LastInsertId.sql";

        private readonly string? _connStr;
        private readonly Func<SqliteConnection>? _openConnection;
        private readonly string _appVersion;
        private readonly string _machineId;

        // Preferred: factory that returns an OPEN, keyed SqliteConnection
        public LogRepository(Func<SqliteConnection> openConnection, string appVersion)
        {
            _openConnection = openConnection ?? throw new ArgumentNullException(nameof(openConnection));
            _appVersion = string.IsNullOrWhiteSpace(appVersion) ? "unknown" : appVersion;
            _machineId = MachineInfo.GetStableMachineId();
        }

        // Optional: connection string (we open it)
        public LogRepository(string connectionString, string appVersion)
        {
            _connStr = string.IsNullOrWhiteSpace(connectionString)
                ? throw new ArgumentNullException(nameof(connectionString))
                : connectionString;
            _appVersion = string.IsNullOrWhiteSpace(appVersion) ? "unknown" : appVersion;
            _machineId = MachineInfo.GetStableMachineId();
        }

        private SqliteConnection Open()
            => _openConnection != null ? _openConnection()
                                       : new SqliteConnection(_connStr!);

        /// <summary>
        /// Serialize payloadObject to JSON (or pass-through if string), encrypt with LogPayloadKey (AES-GCM),
        /// and insert a log row. Returns inserted Id or 0 on script-missing.
        /// </summary>
        public async System.Threading.Tasks.Task<long> LogAsync(
            LogLevel level,
            string source,
            string eventCode,
            object payloadObject,
            bool isCrash = false,
            string? sessionId = null,
            string? stackHash = null,
            string? correlationId = null, // reserved
            int payloadVer = 1,           // reserved (always 1 in dev)
            int keySetVersion = 1)        // reserved (always 1 in dev)
        {
            string? insertSql = null;
            string? lastIdSql = null;
            try
            {
                insertSql = SecureSql.Require(SqlInsertKey);
                lastIdSql = SecureSql.Require(SqlLastInsertIdKey);
            }
            catch
            {
                // Missing scripts — skip quietly, stay non-fatal for app
                return 0L;
            }

            // Build plaintext JSON payload
            string json = payloadObject switch
            {
                null => "",
                string s => s,
                _ => JsonSerializer.Serialize(payloadObject,
                        new JsonSerializerOptions { WriteIndented = false })
            };
            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);

            // Encrypt with LogPayloadKey -> BLOB (nonce|ct|tag)
            byte[] payload = CryptoLogCodec.Encrypt(jsonBytes);

            // Normalize metadata
            string safeSource = source ?? string.Empty;
            string safeEventCode = eventCode ?? string.Empty;
            string safeSessionId = sessionId ?? string.Empty;
            string levelText = level switch
            {
                LogLevel.Trace => "TRACE",
                LogLevel.Info => "INFO",
                LogLevel.Warn => "WARN",
                LogLevel.Error => "ERROR",
                LogLevel.Critical => "FATAL",
                _ => "INFO"
            };

            using var cn = Open();
            if (_openConnection == null) await cn.OpenAsync().ConfigureAwait(false);

            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = insertSql!;

                cmd.Parameters.AddWithValue("@WhenUtc", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
                cmd.Parameters.AddWithValue("@CreatedUtc", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
                cmd.Parameters.AddWithValue("@Level", levelText);
                cmd.Parameters.AddWithValue("@Source", safeSource);
                cmd.Parameters.AddWithValue("@EventCode", safeEventCode);
                cmd.Parameters.AddWithValue("@SessionId", safeSessionId);
                cmd.Parameters.AddWithValue("@MachineId", _machineId ?? string.Empty);
                cmd.Parameters.AddWithValue("@AppVersion", _appVersion ?? string.Empty);
                cmd.Parameters.AddWithValue("@IsCrash", isCrash ? 1 : 0);

                // @Payload as BLOB
                var p = cmd.CreateParameter();
                p.ParameterName = "@Payload";
                p.SqliteType = SqliteType.Blob;
                p.Value = payload ?? Array.Empty<byte>();
                cmd.Parameters.Add(p);

                // @PayloadFmt
                cmd.Parameters.AddWithValue("@PayloadFmt", "gcm-json-v1");
                cmd.Parameters.AddWithValue("@StackHash", (object?)stackHash ?? DBNull.Value);

                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            using var idCmd = cn.CreateCommand();
            idCmd.CommandText = lastIdSql!;
            var idObj = await idCmd.ExecuteScalarAsync().ConfigureAwait(false);
            return idObj is long id ? id : Convert.ToInt64(idObj ?? 0L);
        }

        /// <summary>
        /// DEBUG helper: fetch recent logs and return decrypted JSON text.
        /// Tolerates both encrypted BLOB (nonce|ct|tag) and legacy TEXT payloads (early-ingest).
        /// </summary>
        public async System.Threading.Tasks.Task<(long Id, string Json)[]> GetRecentAsync(int count = 50, bool crashesOnly = false)
        {
            var list = new List<(long, string)>();
            string? selectSql = null;
            try { selectSql = SecureSql.Require(SqlSelectRecentKey); }
            catch { return list.ToArray(); } // script missing — return empty

            using var cn = Open();
            if (_openConnection == null) await cn.OpenAsync().ConfigureAwait(false);

            using var cmd = cn.CreateCommand();
            cmd.CommandText = selectSql!;
            cmd.Parameters.AddWithValue("@Limit", count);
            cmd.Parameters.AddWithValue("@CrashesOnly", crashesOnly ? 1 : (object)DBNull.Value);
            // Optional: @FromUtc can be bound by caller via different overload, omitted here.

            using var rd = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            // Try to locate payload column robustly
            int payloadOrd = -1;
            try { payloadOrd = rd.GetOrdinal("Payload"); } catch { /* fall back */ }

            while (await rd.ReadAsync().ConfigureAwait(false))
            {
                long id = 0;
                try
                {
                    // Id is usually the first column
                    id = rd.GetInt64(0);
                }
                catch { /* best effort */ }

                string json = "";
                try
                {
                    object cell;
                    if (payloadOrd >= 0)
                    {
                        cell = rd.GetValue(payloadOrd);
                    }
                    else
                    {
                        // Fallback: choose first BLOB-looking column, else first string
                        object chosen = null;
                        for (int i = 0; i < rd.FieldCount; i++)
                        {
                            var val = rd.GetValue(i);
                            if (val is byte[]) { chosen = val; break; }
                            if (chosen == null && val is string) chosen = val;
                        }
                        cell = chosen ?? "";
                    }

                    if (cell is byte[] blob && blob.Length >= 12 + 16)
                    {
                        var pt = CryptoLogCodec.Decrypt(blob);
                        json = Encoding.UTF8.GetString(pt);
                    }
                    else if (cell is string s)
                    {
                        // legacy/plain payload
                        json = s;
                    }
                }
                catch
                {
                    json = "";
                }

                list.Add((id, json));
            }
            return list.ToArray();
        }

        // =========================
        //   Machine identification
        // =========================
        private static class MachineInfo
        {
            public static string GetStableMachineId()
            {
                using var sha = SHA256.Create();
                var data = Encoding.UTF8.GetBytes($"{GetSystemDriveSerial() ?? ""}|{GetMachineGuid() ?? ""}");
                return Convert.ToHexString(sha.ComputeHash(data)).Substring(0, 16);
            }

            private static string? GetMachineGuid()
            {
                try
                {
                    using var rk = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                    return rk?.GetValue("MachineGuid") as string;
                }
                catch { return null; }
            }

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern bool GetVolumeInformationW(
                string lpRootPathName,
                StringBuilder lpVolumeNameBuffer,
                int nVolumeNameSize,
                out uint lpVolumeSerialNumber,
                out uint lpMaximumComponentLength,
                out uint lpFileSystemFlags,
                StringBuilder lpFileSystemNameBuffer,
                int nFileSystemNameSize);

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

        // =========================
        //     Crypto for payload
        // =========================
        private static class CryptoLogCodec
        {
            private static byte[] GetLogKey()
            {
                // Must be provisioned by KeyProvisioner into SecureEncryptedDataStore
                if (!SecureEncryptedDataStore.HasKey("LogPayloadKey"))
                    throw new InvalidOperationException("LogPayloadKey not loaded into SecureEncryptedDataStore.");
                var key = SecureEncryptedDataStore.GetBytes("LogPayloadKey");
                if (key == null || key.Length != 32)
                    throw new InvalidOperationException("LogPayloadKey is missing or not 32 bytes.");
                return key;
            }

            public static byte[] Encrypt(byte[] plaintext)
            {
                if (plaintext == null) plaintext = Array.Empty<byte>();
                byte[] key = GetLogKey();
                byte[] iv = RandomNumberGenerator.GetBytes(12);
                byte[] ct = new byte[plaintext.Length];
                byte[] tag = new byte[16];

                using var aes = new AesGcm(key);
                aes.Encrypt(iv, plaintext, ct, tag);

                var output = new byte[iv.Length + ct.Length + tag.Length];
                Buffer.BlockCopy(iv, 0, output, 0, iv.Length);
                Buffer.BlockCopy(ct, 0, output, iv.Length, ct.Length);
                Buffer.BlockCopy(tag, 0, output, iv.Length + ct.Length, tag.Length);
                return output;
            }

            public static byte[] Decrypt(byte[] blob)
            {
                if (blob == null || blob.Length < 12 + 16)
                    return Array.Empty<byte>();

                byte[] key = GetLogKey();
                byte[] iv = new byte[12];
                byte[] tag = new byte[16];
                int ctLen = blob.Length - iv.Length - tag.Length;
                byte[] ct = new byte[ctLen];
                Buffer.BlockCopy(blob, 0, iv, 0, iv.Length);
                Buffer.BlockCopy(blob, iv.Length, ct, 0, ctLen);
                Buffer.BlockCopy(blob, iv.Length + ctLen, tag, 0, tag.Length);

                byte[] pt = new byte[ctLen];
                using var aes = new AesGcm(key);
                aes.Decrypt(iv, ct, tag, pt);
                return pt;
            }
        }
    }
}
