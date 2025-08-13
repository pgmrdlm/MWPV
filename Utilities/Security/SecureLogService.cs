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

namespace Utilities.Security
{
    public enum LogLevel { Debug, Info, Warn, Error, Fatal }

    /// <summary>
    /// Secure, parameterized logging to the local encrypted SQLite database.
    /// - Payloads are JSON encrypted with AES-GCM using provisioned LogPayloadKey.
    /// - New rows are tagged with PayloadVer and KeySetVersion for forward compatibility.
    /// - SQL templates (e.g., "Logs_Insert_V2.sql") are loaded from SecureEncryptedDataStore,
    ///   with a generic version-aware resolver that picks the highest available "…_V#.sql" or falls back to "… .sql".
    /// </summary>
    public static class SecureLogService
    {
        /// <summary>
        /// Logical (base) name for the insert template. The resolver will look for:
        ///   - "Logs_Insert_V{n}.sql" (highest n wins), then
        ///   - "Logs_Insert.sql"
        /// </summary>
        private const string InsertBaseName = "Logs_Insert";

        /// <summary>
        /// Bump when payload format or key policy changes.
        /// </summary>
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

        /// <summary>
        /// Initialize the service. Call once after the database is ready.
        /// </summary>
        public static void Initialize(Func<SqliteConnection> openAppConnection,
                                      string? appVersion = null,
                                      string? defaultSource = null)
        {
            _openAppConnection = openAppConnection ?? throw new ArgumentNullException(nameof(openAppConnection));
            _appVersion = string.IsNullOrWhiteSpace(appVersion)
                ? (Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0")
                : appVersion!;
            if (!string.IsNullOrWhiteSpace(defaultSource)) _defaultSource = defaultSource!;
            _sessionId = Guid.NewGuid().ToString("N");
        }

        /// <summary>
        /// Write a log row. Never throws; returns false on failure.
        /// </summary>
        public static async Task<bool> WriteAsync(
            LogLevel level,
            object? payload = null,
            string? eventCode = null,
            string? source = null,
            Exception? ex = null,
            bool isCrash = false,
            CancellationToken ct = default)
        {
            if (_openAppConnection is null) return false;

            byte[]? plain = null;
            try
            {
                // Compose JSON payload (include exception details if provided)
                var body = ex == null ? payload :
                    new
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

                // Normalize inputs
                string levelText = level.ToString().ToUpperInvariant(); // DEBUG|INFO|WARN|ERROR|FATAL
                string src = string.IsNullOrWhiteSpace(source) ? _defaultSource : source!;
                string machId = ComputeMachineIdHash();
                string stackHash = ex?.StackTrace is string st ? Sha256Hex(st) : "";

                // Resolve the insert SQL from the secure store (version-aware)
                string? sql = ResolveSqlTemplate(InsertBaseName);
                if (string.IsNullOrWhiteSpace(sql))
                    return false; // template missing → skip quietly

                using var cn = _openAppConnection();
                using var cmd = cn.CreateCommand();
                cmd.CommandText = sql;

                // Bind parameters expected by Logs_Insert_V2.sql (current schema)
                cmd.Parameters.AddWithValue("@CreatedUtc", DateTime.UtcNow.ToString("o")); // TEXT ISO-8601
                cmd.Parameters.AddWithValue("@Level", levelText ?? "");
                cmd.Parameters.AddWithValue("@Source", (object?)src ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@EventCode", (object?)eventCode ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@SessionId", (object?)_sessionId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@MachineId", (object?)machId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@AppVersion", (object?)_appVersion ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@IsCrash", isCrash ? 1 : 0);

                var p = cmd.CreateParameter();
                p.ParameterName = "@Payload";
                p.SqliteType = SqliteType.Blob;
                p.Value = (object?)encBlob ?? Array.Empty<byte>();
                cmd.Parameters.Add(p);

                cmd.Parameters.AddWithValue("@PayloadFmt", "json+aesgcm");
                cmd.Parameters.AddWithValue("@StackHash", (object?)stackHash ?? DBNull.Value);


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

        // ---------- SQL template resolution ----------

        /// <summary>
        /// Resolve a SQL template by base name from SecureEncryptedDataStore.
        /// Looks for "{base}.sql" and "{base}_V{n}.sql" (highest n wins), case-insensitive.
        /// Returns null if nothing is found.
        /// </summary>
        public static string? ResolveSqlTemplate(string baseName)
        {
            if (string.IsNullOrWhiteSpace(baseName)) return null;

            // Fast path: exact "{base}.sql"
            string exact = baseName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)
                ? baseName
                : baseName + ".sql";

            var exactSql = SecureEncryptedDataStore.GetString(exact);
            if (!string.IsNullOrWhiteSpace(exactSql))
                return exactSql;

            // Versioned scan: "{base}_V{n}.sql" → pick highest n
            // Example: "Logs_Insert_V2.sql", "Logs_Insert_V3.sql"
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

        // ---------- AES-GCM helpers ----------

        /// <summary>
        /// Encrypts the payload with AES-GCM using the provisioned LogPayloadKey.
        /// Layout: IV(12) | CIPHERTEXT | TAG(16).
        /// </summary>
        private static byte[] EncryptPayload(ReadOnlySpan<byte> plaintext)
        {
            byte[] key = GetLogKey();            // provisioned AES-256 key (or legacy fallback)
            byte[] iv = new byte[12];           // 96-bit nonce
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

        /// <summary>
        /// Returns the current KeySetVersion from the secure store (defaults to 1).
        /// </summary>
        private static int CurrentKeySetVersion()
        {
            var s = SecureEncryptedDataStore.GetString("KeySetVersion");
            return int.TryParse(s, out var v) && v > 0 ? v : 1;
        }

        /// <summary>
        /// Gets the provisioned LogPayloadKey from the secure store; falls back to legacy machine-bound derivation.
        /// </summary>
        private static byte[] GetLogKey()
        {
            if (SecureEncryptedDataStore.HasKey("LogPayloadKey"))
                return SecureEncryptedDataStore.GetBytes("LogPayloadKey");

            // Legacy fallback so older installs still work (machine-bound)
            return DeriveKey32();
        }

        // ---------- Legacy key material (fallback) ----------

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
    }
}
