using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Utilities.Logging; // LogSeverity, LogEventIds

namespace Utilities.Security
{
    // Kept for backward compatibility with existing callers (e.g., App alias).
    public enum LogLevel { Debug, Info, Warn, Error, Fatal }

    /// <summary>
    /// Encrypted, parameterized logging to the app's SQLite DB.
    /// - Payloads are JSON, encrypted via AES-GCM with LogPayloadKey (fallback: legacy machine-bound key).
    /// - SQL is loaded from SecureEncryptedDataStore (version-aware resolver: Logs_Insert_V{n}.sql).
    /// - Binds only parameters that exist in the selected SQL (safe across schema versions).
    /// </summary>
    public static class SecureLogService
    {
        private const string InsertBaseName = "Logs_Insert";

        /// <summary>Bump when payload format or key policy changes.</summary>
        private const int PayloadVer = 2;

        private static Func<SqliteConnection>? _openAppConnection;
        private static string _appVersion = "0.0.0";
        private static string _defaultSource = "MWPV";
        private static string _sessionId = Guid.NewGuid().ToString("N");

        private static readonly JsonSerializerOptions _json = new()
        {
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        public static void Initialize(
            Func<SqliteConnection> openAppConnection,
            string? appVersion = null,
            string? defaultSource = null)
        {
            _openAppConnection = openAppConnection ?? throw new ArgumentNullException(nameof(openAppConnection));
            _appVersion = string.IsNullOrWhiteSpace(appVersion)
                ? (Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0")
                : appVersion!;
            if (!string.IsNullOrWhiteSpace(defaultSource))
                _defaultSource = defaultSource!;
            _sessionId = Guid.NewGuid().ToString("N");
        }

        // ---------------------- Public write APIs ----------------------

        // New API using Utilities.Logging.LogSeverity + optional numeric EventId
        public static Task<bool> WriteAsync(
            LogSeverity level,
            object? payload = null,
            int? eventId = null,
            string? source = null,
            Exception? ex = null,
            bool isCrash = false,
            CancellationToken ct = default)
            => WriteCoreAsync(Map(level), payload, eventId, eventCode: null, source, ex, isCrash, ct);

        // Back-compat API (string eventCode and old enum)
        public static Task<bool> WriteAsync(
            LogLevel level,
            object? payload = null,
            string? eventCode = null,
            string? source = null,
            Exception? ex = null,
            bool isCrash = false,
            CancellationToken ct = default)
            => WriteCoreAsync(level, payload, eventId: null, eventCode, source, ex, isCrash, ct);

        // ---------------------- Core implementation ----------------------

        private static async Task<bool> WriteCoreAsync(
            LogLevel level,
            object? payload,
            int? eventId,
            string? eventCode,
            string? source,
            Exception? ex,
            bool isCrash,
            CancellationToken ct)
        {
            if (_openAppConnection is null) return false;

            byte[]? plain = null;
            try
            {
                // Compose JSON payload (+ exception if present)
                var body = ex == null
                    ? payload
                    : new
                    {
                        payload,
                        exception = new
                        {
                            type = ex.GetType().FullName,
                            message = ex.Message,
                            stack = ex.StackTrace
                        }
                    };

                string json = body is null ? "{}" : JsonSerializer.Serialize(body, _json);
                plain = Encoding.UTF8.GetBytes(json);
                byte[] encBlob = EncryptPayload(plain);

                string levelText = level.ToString().ToUpperInvariant(); // DEBUG|INFO|WARN|ERROR|FATAL
                string src = string.IsNullOrWhiteSpace(source) ? _defaultSource : source!;
                string machId = ComputeMachineIdHash();
                string stackHash = ex?.StackTrace is string st ? Sha256Hex(st) : "";

                // Resolve SQL
                string? sql = ResolveSqlTemplate(InsertBaseName);
                if (string.IsNullOrWhiteSpace(sql))
                    return false;

                using var cn = _openAppConnection();
                using var cmd = cn.CreateCommand();
                cmd.CommandText = sql;

                // Bind with tolerant/synonym helpers
                AddFirstMatch(cmd, sql, new[] { "@CreatedUtc", "@TimestampUtc" }, DateTime.UtcNow.ToString("o"));
                AddFirstMatch(cmd, sql, new[] { "@Level", "@Severity" }, levelText);
                AddFirstMatch(cmd, sql, new[] { "@Source" }, src);

                if (eventId.HasValue)
                    AddIfPresent(cmd, sql, "@EventId", eventId.Value);
                if (!string.IsNullOrWhiteSpace(eventCode))
                    AddIfPresent(cmd, sql, "@EventCode", eventCode);

                AddFirstMatch(cmd, sql, new[] { "@SessionId", "@CorrelationId" }, _sessionId);
                AddIfPresent(cmd, sql, "@MachineId", machId);
                AddIfPresent(cmd, sql, "@AppVersion", _appVersion);
                AddIfPresent(cmd, sql, "@IsCrash", isCrash ? 1 : 0);

                AddBlobIfPresent(cmd, sql, "@Payload", encBlob);
                AddIfPresent(cmd, sql, "@PayloadFmt", "json+aesgcm");

                // Versioning / key metadata (only if the SQL expects them)
                AddIfPresent(cmd, sql, "@PayloadVer", PayloadVer);
                AddIfPresent(cmd, sql, "@KeySetVersion", CurrentKeySetVersion());

                // Optional stack correlation
                if (!string.IsNullOrEmpty(stackHash))
                    AddIfPresent(cmd, sql, "@StackHash", stackHash);

                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return true;
            }
            catch
            {
                // Logging must never throw.
                return false;
            }
            finally
            {
                if (plain != null) SensitiveDataCleaner.WipeByteArray(plain);
            }
        }

        private static LogLevel Map(LogSeverity s) => s switch
        {
            LogSeverity.Debug => LogLevel.Debug,
            LogSeverity.Info => LogLevel.Info,
            LogSeverity.Warn => LogLevel.Warn,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Critical => LogLevel.Fatal,
            _ => LogLevel.Info
        };

        // ---------------------- SQL template resolution ----------------------

        /// <summary>
        /// Resolve a SQL template by base name from SecureEncryptedDataStore.
        /// Tries "{base}.sql" then scans for highest "{base}_V{n}.sql".
        /// </summary>
        public static string? ResolveSqlTemplate(string baseName)
        {
            if (string.IsNullOrWhiteSpace(baseName)) return null;

            string exact = baseName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)
                ? baseName
                : baseName + ".sql";

            var exactSql = SecureEncryptedDataStore.GetString(exact);
            if (!string.IsNullOrWhiteSpace(exactSql))
                return exactSql;

            var keys = SecureEncryptedDataStore.Keys();
            if (keys is null) return null;

            var rx = new Regex(
                $"^{Regex.Escape(baseName)}_V(\\d+)\\.sql$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            int bestVer = -1;
            string? bestKey = null;

            foreach (var key in keys)
            {
                if (key is null) continue;
                var m = rx.Match(key);
                if (!m.Success) continue;
                if (int.TryParse(m.Groups[1].Value, out int ver) && ver > bestVer)
                {
                    bestVer = ver;
                    bestKey = key;
                }
            }

            return bestKey is null ? null : SecureEncryptedDataStore.GetString(bestKey);
        }

        // ---------------------- AES-GCM helpers ----------------------

        /// <summary>Encrypt payload with AES-GCM using provisioned LogPayloadKey. Layout: IV(12)|CIPHERTEXT|TAG(16).</summary>
        private static byte[] EncryptPayload(ReadOnlySpan<byte> plaintext)
        {
            byte[] key = GetLogKey();
            byte[] iv = new byte[12];
            RandomNumberGenerator.Fill(iv);

            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[16];

            using (var gcm = new AesGcm(key))
            {
                gcm.Encrypt(iv, plaintext, ciphertext, tag);
            }

            byte[] blob = new byte[iv.Length + ciphertext.Length + tag.Length];
            Buffer.BlockCopy(iv, 0, blob, 0, iv.Length);
            Buffer.BlockCopy(ciphertext, 0, blob, iv.Length, ciphertext.Length);
            Buffer.BlockCopy(tag, 0, blob, iv.Length + ciphertext.Length, tag.Length);

            Array.Clear(ciphertext, 0, ciphertext.Length);
            Array.Clear(tag, 0, tag.Length);
            return blob;
        }

        private static int CurrentKeySetVersion()
        {
            var s = SecureEncryptedDataStore.GetString("KeySetVersion");
            return int.TryParse(s, out var v) && v > 0 ? v : 1;
        }

        private static byte[] GetLogKey()
        {
            if (SecureEncryptedDataStore.HasKey("LogPayloadKey"))
                return SecureEncryptedDataStore.GetBytes("LogPayloadKey");

            // Legacy fallback (machine-bound)
            return DeriveKey32();
        }

        // ---------------------- Legacy key material (fallback) ----------------------

        private static byte[] DeriveKey32()
        {
            string machineGuid = Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", "")?.ToString() ?? "";
            string driveSerial = GetSystemDriveSerial() ?? "";
            string material = $"{driveSerial}|{machineGuid}";

            using var kdf = new Rfc2898DeriveBytes(
                password: Encoding.UTF8.GetBytes(material),
                salt: Encoding.UTF8.GetBytes("b1b9a2d6-5d6a-4c5b-9c24-1f2b7a8a8f13"),
                iterations: 100_000,
                hashAlgorithm: HashAlgorithmName.SHA256);
            return kdf.GetBytes(32);
        }

        private static string ComputeMachineIdHash()
        {
            string machineGuid = Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", "")?.ToString() ?? "";
            string driveSerial = GetSystemDriveSerial() ?? "";
            string material = $"{driveSerial}|{machineGuid}";
            return Sha256Hex(material);
        }

        private static string Sha256Hex(string s)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

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

        // ---------------------- Parameter helpers ----------------------

        private static bool SqlHasParam(string sql, string paramName)
            => sql?.IndexOf(paramName, StringComparison.OrdinalIgnoreCase) >= 0;

        private static void AddIfPresent(SqliteCommand cmd, string sql, string name, object? value)
        {
            if (!SqlHasParam(sql, name)) return;
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        private static void AddBlobIfPresent(SqliteCommand cmd, string sql, string name, byte[] blob)
        {
            if (!SqlHasParam(sql, name)) return;
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.SqliteType = SqliteType.Blob;
            p.Value = blob ?? Array.Empty<byte>();
            cmd.Parameters.Add(p);
        }

        private static void AddFirstMatch(SqliteCommand cmd, string sql, string[] names, object? value)
        {
            foreach (var n in names)
            {
                if (SqlHasParam(sql, n))
                {
                    cmd.Parameters.AddWithValue(n, value ?? DBNull.Value);
                    return;
                }
            }
        }
    }
}
