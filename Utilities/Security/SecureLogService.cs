using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Utilities.Security
{
    public enum LogLevel { Debug, Info, Warn, Error, Fatal }

    public static class SecureLogService
    {
        private static Func<SqliteConnection>? _openAppConnection;
        private static string _appVersion = "0.0.0";
        private static string _defaultSource = "MWPV";
        private static string _sessionId = Guid.NewGuid().ToString("N");
        private static readonly JsonSerializerOptions _json = new()
        {
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

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
                byte[] plain = Encoding.UTF8.GetBytes(json);
                byte[] encBlob = EncryptPayload(plain);

                string levelText = level.ToString().ToUpperInvariant();
                string src = string.IsNullOrWhiteSpace(source) ? _defaultSource : source!;
                string machId = ComputeMachineIdHash();
                string stackHash = ex?.StackTrace is string st ? Sha256Hex(st) : "";

                using var cn = _openAppConnection();
                using var cmd = cn.CreateCommand();
                cmd.CommandText = @"
INSERT INTO ""Logs""
(""CreatedUtc"", ""Level"", ""Source"", ""EventCode"", ""SessionId"", ""MachineId"",
 ""AppVersion"", ""IsCrash"", ""Payload"", ""PayloadFmt"", ""StackHash"")
VALUES ($ts, $lv, $src, $evt, $sid, $mid, $ver, $cr, $pld, $fmt, $sh);";

                cmd.Parameters.AddWithValue("$ts", DateTime.UtcNow.ToString("o"));
                cmd.Parameters.AddWithValue("$lv", levelText);
                cmd.Parameters.AddWithValue("$src", src);
                cmd.Parameters.AddWithValue("$evt", eventCode ?? "");
                cmd.Parameters.AddWithValue("$sid", _sessionId);
                cmd.Parameters.AddWithValue("$mid", machId);
                cmd.Parameters.AddWithValue("$ver", _appVersion);
                cmd.Parameters.AddWithValue("$cr", isCrash ? 1 : 0);
                cmd.Parameters.Add("$pld", SqliteType.Blob).Value = encBlob;   // ensure BLOB storage
                cmd.Parameters.AddWithValue("$fmt", "json+aesgcm");
                cmd.Parameters.AddWithValue("$sh", stackHash);

                _ = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return true;
            }
            catch
            {
                // Logging must never throw.
                return false;
            }
        }

        // ---------- AES-GCM helpers ----------
        private static byte[] EncryptPayload(ReadOnlySpan<byte> plaintext)
        {
            byte[] key = DeriveKey32();
            byte[] iv = new byte[12];
            RandomNumberGenerator.Fill(iv);

            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[16];

            using var aes = new AesGcm(key);
            aes.Encrypt(iv, plaintext, ciphertext, tag);

            byte[] blob = new byte[iv.Length + ciphertext.Length + tag.Length];
            Buffer.BlockCopy(iv, 0, blob, 0, iv.Length);
            Buffer.BlockCopy(ciphertext, 0, blob, iv.Length, ciphertext.Length);
            Buffer.BlockCopy(tag, 0, blob, iv.Length + ciphertext.Length, tag.Length);
            return blob;
        }

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
