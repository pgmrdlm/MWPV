// MWPV/Services/LogRepository.cs
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Utilities.Sql; // SecureSql

namespace MWPV.Services
{
    public enum LogLevel { Trace, Info, Warn, Error, Critical }

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
            _machineId = MachineKeyProvider.GetStableMachineId();
        }

        // Optional: connection string (we open it)
        public LogRepository(string connectionString, string appVersion)
        {
            _connStr = string.IsNullOrWhiteSpace(connectionString) ? throw new ArgumentNullException(nameof(connectionString)) : connectionString;
            _appVersion = string.IsNullOrWhiteSpace(appVersion) ? "unknown" : appVersion;
            _machineId = MachineKeyProvider.GetStableMachineId();
        }

        private SqliteConnection Open()
            => _openConnection != null ? _openConnection()
                                       : new SqliteConnection(_connStr!);

        public async System.Threading.Tasks.Task<long> LogAsync(
            LogLevel level,
            string source,
            string eventCode,
            object payloadObject,
            bool isCrash = false,
            string? sessionId = null,
            string? stackHash = null,
            string? correlationId = null, // reserved
            int payloadVer = 1,           // reserved
            int keySetVersion = 1)        // reserved
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
                // Missing scripts — skip quietly
                return 0L;
            }

            // Serialize -> encrypt (machine-bound AES-GCM: IV|CT|TAG)
            var json = JsonSerializer.Serialize(payloadObject, new JsonSerializerOptions { WriteIndented = false });
            var payload = CryptoLogCodec.Encrypt(Encoding.UTF8.GetBytes(json));

            using var cn = Open();
            if (_openConnection == null) await cn.OpenAsync();

            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = insertSql;

                // timestamps as ISO-8601 text (schema uses TEXT)
                var nowIso = DateTime.UtcNow.ToString("o");
                cmd.Parameters.AddWithValue("@WhenUtc", nowIso);
                cmd.Parameters.AddWithValue("@CreatedUtc", nowIso);

                // coalesce NOT NULL columns to safe values
                var safeSource = string.IsNullOrWhiteSpace(source) ? "app" : source;
                var safeEventCode = string.IsNullOrWhiteSpace(eventCode) ? "GENERAL" : eventCode;
                var safeSessionId = string.IsNullOrWhiteSpace(sessionId) ? "startup" : sessionId;

                // core fields
                cmd.Parameters.AddWithValue("@Level", MapLevel(level));
                cmd.Parameters.AddWithValue("@Source", safeSource);
                cmd.Parameters.AddWithValue("@EventCode", safeEventCode);
                cmd.Parameters.AddWithValue("@SessionId", safeSessionId);
                cmd.Parameters.AddWithValue("@MachineId", _machineId ?? string.Empty);
                cmd.Parameters.AddWithValue("@AppVersion", _appVersion ?? string.Empty);
                cmd.Parameters.AddWithValue("@IsCrash", isCrash ? 1 : 0);

                // payload (BLOB)
                var p = cmd.CreateParameter();
                p.ParameterName = "@Payload";
                p.SqliteType = SqliteType.Blob;
                p.Value = payload ?? Array.Empty<byte>();
                cmd.Parameters.Add(p);

                // metadata
                cmd.Parameters.AddWithValue("@PayloadFmt", "json+aesgcm");
                cmd.Parameters.AddWithValue("@StackHash", (object?)stackHash ?? DBNull.Value);

                await cmd.ExecuteNonQueryAsync();
            }

            using var idCmd = cn.CreateCommand();
            idCmd.CommandText = lastIdSql!;
            var idObj = await idCmd.ExecuteScalarAsync();
            return idObj is long id ? id : Convert.ToInt64(idObj ?? 0L);
        }

        public async System.Threading.Tasks.Task<(long Id, string Json)[]> GetRecentAsync(int count = 50, bool crashesOnly = false)
        {
            var list = new List<(long, string)>();

            string? selectSql = null;
            try { selectSql = SecureSql.Require(SqlSelectRecentKey); }
            catch { return list.ToArray(); } // script missing — return empty

            using var cn = Open();
            if (_openConnection == null) await cn.OpenAsync();

            using var cmd = cn.CreateCommand();
            cmd.CommandText = selectSql;
            cmd.Parameters.AddWithValue("@Limit", count);
            // Pass NULL for "all", 1 for crashes only (matches WHERE (@CrashesOnly IS NULL OR IsCrash = @CrashesOnly))
            cmd.Parameters.AddWithValue("@CrashesOnly", crashesOnly ? 1 : (object)DBNull.Value);

            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                long id = rd.GetInt64(0);
                var blob = (byte[])rd[1];
                var jsonBytes = CryptoLogCodec.Decrypt(blob);
                list.Add((id, Encoding.UTF8.GetString(jsonBytes)));
            }
            return list.ToArray();
        }

        // ===== internal helpers =====

        private static string MapLevel(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRACE",
            LogLevel.Info => "INFO",
            LogLevel.Warn => "WARN",
            LogLevel.Error => "ERROR",
            LogLevel.Critical => "FATAL",
            _ => "INFO"
        };

        private static class MachineKeyProvider
        {
            private const string AppSalt = "b1b9a2d6-5d6a-4c5b-9c24-1f2b7a8a8f13"; // constant app GUID

            public static byte[] DeriveKey32()
            {
                var driveSerial = GetSystemDriveSerial_PInvoke() ?? "noserial";
                var machineGuid = GetMachineGuid() ?? "noguid";
                var material = Encoding.UTF8.GetBytes(driveSerial + "|" + machineGuid);

                using var kdf = new Rfc2898DeriveBytes(material, Encoding.UTF8.GetBytes(AppSalt), 100_000, HashAlgorithmName.SHA256);
                return kdf.GetBytes(32);
            }

            public static string GetStableMachineId()
            {
                using var sha = SHA256.Create();
                var data = Encoding.UTF8.GetBytes((GetSystemDriveSerial_PInvoke() ?? "") + "|" + (GetMachineGuid() ?? ""));
                return Convert.ToHexString(sha.ComputeHash(data)).Substring(0, 16);
            }

            private static string? GetMachineGuid()
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                    return key?.GetValue("MachineGuid")?.ToString();
                }
                catch { return null; }
            }

            // Windows P/Invoke to avoid System.Management/WMI dependency
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

            private static string? GetSystemDriveSerial_PInvoke()
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

        private static class CryptoLogCodec
        {
            public static byte[] Encrypt(byte[] plaintext)
            {
                // TODO: swap to LogPayloadKey once provisioned; this is the current machine-bound encryption
                byte[] key = MachineKeyProvider.DeriveKey32();
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
                byte[] key = MachineKeyProvider.DeriveKey32();
                byte[] iv = blob[..12];
                byte[] tag = blob[^16..];
                byte[] ct = blob[12..^16];
                byte[] pt = new byte[ct.Length];

                using var aes = new AesGcm(key);
                aes.Decrypt(iv, ct, tag, pt);
                return pt;
            }
        }
    }
}
