using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

namespace Utilities.Diagnostics
{
    /// <summary>
    /// Writes and manages pre-login *.elog files in:
    ///   %LOCALAPPDATA%\MWPV\early\
    ///
    /// File format (v1):
    ///   MWPV-ELOG|v1
    ///   content-type: application/json; charset=utf-8
    ///
    ///   {json payload}
    ///
    /// JSON payload shape (example):
    ///   { "version":1,"utc":"2025-08-22T14:49:22.993Z","type":"invalid-password-or-keyfile",
    ///     "sessionId":"<hex>","machine":"MYPC","userSid":"...","userName":"...","details":"..." }
    /// </summary>
    public static class EarlyLoginFailures
    {
        // ----- locations -----
        public static readonly string StoreDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MWPV", "early");

        public static readonly string QuarantineDir =
            Path.Combine(StoreDir, "quarantine");

        // Session Id for this process (hex). App can overwrite if desired.
        public static string SessionId { get; set; } = NewSessionId();

        // File header constants
        private const string V1Header = "MWPV-ELOG|v1";
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        static EarlyLoginFailures()
        {
            try { Directory.CreateDirectory(StoreDir); } catch { /* swallow */ }
            try { Directory.CreateDirectory(QuarantineDir); } catch { /* swallow */ }
        }

        /// <summary>Used by UI to decide whether to show the post-login “ingesting early files” notice.</summary>
        public static bool HasPending()
        {
            try { return Directory.EnumerateFiles(StoreDir, "*.elog", SearchOption.TopDirectoryOnly).Any(); }
            catch { return false; }
        }

        /// <summary>Enumerate pending *.elog paths (sorted by name/time ascending).</summary>
        public static IEnumerable<string> EnumeratePendingPaths()
        {
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(StoreDir, "*.elog", SearchOption.TopDirectoryOnly); }
            catch { yield break; }

            foreach (var f in files.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                yield return f;
        }

        /// <summary>
        /// Record an early failure event. Returns path on success, or null on total failure (never throws).
        /// </summary>
        public static string? Record(EarlyFailType type, string? message = null, Exception? ex = null, object? extra = null)
        {
            try
            {
                Directory.CreateDirectory(StoreDir);

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

                // Stable name: timestamp + type + short content hash
                var sig = ToHex(SHA256.HashData(Utf8NoBom.GetBytes(json)));
                var name = $"{nowUtc:yyyyMMddTHHmmssfff}_{payload.type}_{sig[..12]}.elog";
                var path = Path.Combine(StoreDir, name);

                using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                using var sw = new StreamWriter(fs, Utf8NoBom) { NewLine = "\r\n" };

                sw.WriteLine(V1Header);
                sw.WriteLine("content-type: application/json; charset=utf-8");
                sw.WriteLine();
                sw.WriteLine(json);

                return path;
            }
            catch
            {
                // Best-effort fallback to temp
                try
                {
                    var temp = Path.Combine(Path.GetTempPath(), "MWPV", "early");
                    Directory.CreateDirectory(temp);
                    var path = Path.Combine(temp, $"fallback_{DateTime.UtcNow:yyyyMMddTHHmmssfff}.elog");
                    File.WriteAllText(path, V1Header + "\r\ncontent-type: application/json; charset=utf-8\r\n\r\n{}", Utf8NoBom);
                    return path;
                }
                catch { return null; }
            }
        }

        /// <summary>
        /// Move a bad/suspect .elog to quarantine (best-effort). If provided, writes a ".reason.txt" sibling.
        /// </summary>
        public static void TryQuarantine(string sourcePath, string? reason = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) return;

                Directory.CreateDirectory(QuarantineDir);

                var name = Path.GetFileNameWithoutExtension(sourcePath);
                var ext = Path.GetExtension(sourcePath);

                var dest = Path.Combine(QuarantineDir, $"{name}{ext}");
                int i = 1;
                while (File.Exists(dest))
                    dest = Path.Combine(QuarantineDir, $"{name}_{i++}{ext}");

                File.Move(sourcePath, dest);

                if (!string.IsNullOrWhiteSpace(reason))
                    File.WriteAllText(dest + ".reason.txt", reason, Utf8NoBom);
            }
            catch { /* swallow */ }
        }

        /// <summary>Alias kept for older call sites.</summary>
        public static void Quarantine(string sourcePath, string? reason = null) => TryQuarantine(sourcePath, reason);

        // ----- helpers -----

        private static string ToSlug(EarlyFailType t)
        {
            // kebab-case from enum name: FooBar -> foo-bar ; InvalidPasswordOrKeyFile -> invalid-password-or-keyfile
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
                else if (c == '_' || c == ' ')
                {
                    sb.Append('-');
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private static string DefaultMessage(EarlyFailType t) =>
            t switch
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
