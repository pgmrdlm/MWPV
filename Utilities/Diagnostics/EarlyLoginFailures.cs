using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using IOPath = System.IO.Path;

namespace Utilities.Diagnostics
{
    // Back-compat: keep EarlyFailType at namespace scope so existing call sites
    // like Utilities.Diagnostics.EarlyFailType continue to compile.
   
    /// <summary>
    /// Writes and manages pre-login *.elog files in:
    ///   %LOCALAPPDATA%\\MWPV\\early\\
    /// </summary>
    public static class EarlyLoginFailures
    {
        // Storage roots
        public static readonly string StoreDir =
            IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MWPV", "early");

        public static readonly string QuarantineDir =
            IOPath.Combine(StoreDir, "quarantine");

        // Session Id for this process (hex). App can overwrite if desired.
        public static string SessionId { get; set; } = NewSessionId();

        // File header constants
        private const string V1Header = "MWPV-ELOG|v1";
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        static EarlyLoginFailures()
        {
            try { Directory.CreateDirectory(StoreDir); } catch { }
            try { Directory.CreateDirectory(QuarantineDir); } catch { }
        }

        public static bool HasPending()
        {
            try { return Directory.EnumerateFiles(StoreDir, "*.elog", SearchOption.TopDirectoryOnly).Any(); }
            catch { return false; }
        }

        public static IEnumerable<string> EnumeratePendingPaths()
        {
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(StoreDir, "*.elog", SearchOption.TopDirectoryOnly); }
            catch { yield break; }
            foreach (var f in files.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                yield return f;
        }

        public static string? Record(EarlyFailType type, string? message = null, Exception? ex = null, object? extra = null)
        {
            try
            {
                var nowUtc = DateTime.UtcNow;
                using var id = WindowsIdentity.GetCurrent();

                var payload = new EarlyFailureV1
                {
                    version = 1,
                    utc = nowUtc.ToString("O"),
                    type = ToSlug(type),
                    sessionId = SessionId,
                    machine = Environment.MachineName,
                    userSid = id?.User?.Value,
                    userName = id?.Name,
                    details = string.IsNullOrWhiteSpace(message) ? DefaultMessage(type) : message!,
                    exception = ex?.ToString(),
                    extra = extra
                };

                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    WriteIndented = false,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                });

                var sig = ToHex(SHA256.HashData(Utf8NoBom.GetBytes(json)));
                var name = $"{nowUtc:yyyyMMddTHHmmssfff}_{payload.type}_{sig[..12]}.elog";
                var path = IOPath.Combine(StoreDir, name);

                using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                using (var sw = new StreamWriter(fs, Utf8NoBom) { NewLine = "\r\n" })
                {
                    sw.WriteLine(V1Header);
                    sw.WriteLine("content-type: application/json; charset=utf-8");
                    sw.WriteLine();
                    sw.WriteLine(json);
                }

                return path;
            }
            catch
            {
                try
                {
                    var temp = IOPath.Combine(IOPath.GetTempPath(), "MWPV", "early");
                    Directory.CreateDirectory(temp);
                    var path = IOPath.Combine(temp, $"fallback_{DateTime.UtcNow:yyyyMMddTHHmmssfff}.elog");

                    var header = V1Header + "\r\n";
                    var contentType = "content-type: application/json; charset=utf-8\r\n";
                    var body = "\r\n{}";

                    File.WriteAllText(path, header + contentType + body, Utf8NoBom);
                    return path;
                }
                catch { return null; }
            }
        }

        public static void TryQuarantine(string sourcePath, string? reason = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) return;

                Directory.CreateDirectory(QuarantineDir);

                var name = IOPath.GetFileNameWithoutExtension(sourcePath);
                var ext = IOPath.GetExtension(sourcePath);

                var dest = IOPath.Combine(QuarantineDir, $"{name}{ext}");
                int i = 1;
                while (File.Exists(dest))
                    dest = IOPath.Combine(QuarantineDir, $"{name}_{i++}{ext}");

                File.Move(sourcePath, dest);

                if (!string.IsNullOrWhiteSpace(reason))
                    File.WriteAllText(dest + ".reason.txt", reason, Utf8NoBom);

                try
                {
                    var fi = new FileInfo(dest);
                    var manifest = new
                    {
                        utc = DateTime.UtcNow.ToString("O"),
                        machine = Environment.MachineName,
                        sessionId = SessionId,
                        originalPath = sourcePath,
                        quarantinedPath = dest,
                        size = fi.Exists ? fi.Length : 0,
                        lastWriteUtc = fi.Exists ? fi.LastWriteTimeUtc.ToString("O") : null,
                        sha256 = fi.Exists ? Sha256HexForFile(dest) : null,
                        reason = reason
                    };
                    var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(dest + ".manifest.json", json, Utf8NoBom);
                }
                catch { }
            }
            catch { }
        }

        public static void Quarantine(string sourcePath, string? reason = null) => TryQuarantine(sourcePath, reason);

        public static bool TryDpapiUnprotect(ReadOnlySpan<byte> protectedBytes, out byte[] plaintext, out string? errorSummary, int maxAttempts = 3)
        {
            plaintext = Array.Empty<byte>();
            errorSummary = null;
            if (protectedBytes.IsEmpty) return true;
            Exception? last = null;
            for (int attempt = 1; attempt <= Math.Max(1, maxAttempts); attempt++)
            {
                try
                {
                    plaintext = ProtectedData.Unprotect(protectedBytes.ToArray(), optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
                    return true;
                }
                catch (CryptographicException ex) { last = ex; Thread.Sleep(50 * attempt); }
                catch (Exception ex) { last = ex; Thread.Sleep(50 * attempt); }
            }
            if (last != null) errorSummary = $"{last.GetType().Name}: {last.Message}";
            return false;
        }

        private static string DefaultMessage(EarlyFailType t) => t switch
        {
            EarlyFailType.InvalidPasswordOrKeyFile => "Invalid Key File Password or invalid key file selected.",
            EarlyFailType.KeyFileVerifyError => "Key file verification error.",
            EarlyFailType.KeyfileMissingOrCorrupt => "Key file missing or corrupt.",
            _ => t.ToString()
        };

        private static string NewSessionId()
        {
            Span<byte> buf = stackalloc byte[16];
            RandomNumberGenerator.Fill(buf);
            return ToHex(buf);
        }

        private static string ToHex(ReadOnlySpan<byte> bytes)
        {
            char[] c = new char[bytes.Length * 2];
            int i = 0;
            foreach (byte b in bytes)
            {
                c[i++] = GetHexNibble(b >> 4);
                c[i++] = GetHexNibble(b & 0xF);
            }
            return new string(c);
            static char GetHexNibble(int v) => (char)(v < 10 ? '0' + v : 'a' + (v - 10));
        }

        private static string Sha256HexForFile(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                return ToHex(SHA256.HashData(fs));
            }
            catch { return string.Empty; }
        }

        private static string ToSlug(EarlyFailType t)
        {
            var s = t.ToString();
            var sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsUpper(c))
                {
                    if (i > 0 && (char.IsLower(s[i - 1]) || (i + 1 < s.Length && char.IsLower(s[i + 1]))))
                        sb.Append('-');
                    sb.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    sb.Append(c == '_' ? '-' : c);
                }
            }
            return sb.ToString();
        }

        private sealed class EarlyFailureV1
        {
            public int version { get; set; }
            public string utc { get; set; } = default!;
            public string type { get; set; } = default!;
            public string sessionId { get; set; } = default!;
            public string machine { get; set; } = default!;
            public string? userSid { get; set; }
            public string? userName { get; set; }
            public string? details { get; set; }
            public string? exception { get; set; }
            public object? extra { get; set; }
        }
    }
}
