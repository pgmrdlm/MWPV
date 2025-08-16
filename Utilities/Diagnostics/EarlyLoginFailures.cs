// Utilities/Diagnostics/EarlyLoginFailures.cs
// Modular early-login failure capture with compatibility shims:
// - DPAPI-protected .elog files under %LOCALAPPDATA%\MWPV\early
// - Dedupe by content hash; quarantine on failure
// - Compatibility members restored: StoreDir, PendingCount (property), FlushToDb(writeDbLog: ...),
//   EarlyFailType.KeyfileMissingOrCorrupt

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;       // ProtectedData, SHA256
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Utilities.Diagnostics
{
    public enum EarlyFailType
    {
        InvalidPasswordOrKeyFile = 1001,
        KeyFileVerifyError = 1002,
        KeyfileMissingOrCorrupt = 1003   // <-- compatibility with existing call sites
    }

    public static class EarlyLoginFailures
    {
        // ---- Constants / paths ------------------------------------------------
        private const string AppFolderName = "MWPV";
        private const string EarlyFolderName = "early";
        private const string QuarantineSubdir = "quarantine";
        private const string FileExt = ".elog";

        private static readonly byte[] OptionalEntropy = Encoding.UTF8.GetBytes("MWPV:EarlyLog:v1");

        /// <summary>Compatibility: path to the early-store directory.</summary>
        public static string StoreDir
        {
            get
            {
                var baseDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName);
                return Path.Combine(baseDir, EarlyFolderName);
            }
        }

        /// <summary>Compatibility: pending file count as a property.</summary>
        public static int PendingCount
        {
            get
            {
                try
                {
                    if (!Directory.Exists(StoreDir)) return 0;
                    return Directory.EnumerateFiles(StoreDir, $"*{FileExt}").Count();
                }
                catch { return 0; }
            }
        }

        // ---- Public API -------------------------------------------------------

        /// <summary>
        /// Write a DPAPI-protected early-failure record. Returns full file path.
        /// Dedupe: if an identical record exists (by content hash), this is a no-op.
        /// </summary>
        public static string Record(EarlyFailType type, string detail, Exception? ex = null)
        {
            EnsureFolders(out var earlyDir, out _);

            var env = EarlyLogEnvelope.Create(type, detail, ex);

            // Canonical JSON for hashing & storage (stable property order)
            var json = JsonSerializer.Serialize(env, JsonOpts);
            var hashHex = Sha256Hex(json);

            // Already captured? skip
            var existing = Directory.EnumerateFiles(earlyDir, $"*_{hashHex}_*{FileExt}").FirstOrDefault();
            if (existing != null) return existing;

            var now = DateTime.UtcNow;
            var name = $"{now:yyyyMMdd_HHmmss_fff}_{hashHex}_{Guid.NewGuid():N}{FileExt}";
            var path = Path.Combine(earlyDir, name);

            var plaintext = Encoding.UTF8.GetBytes(json);
            var ciphertext = Protect(plaintext);

            File.WriteAllBytes(path, ciphertext);
            return path;
        }

        /// <summary>True if any .elog files exist.</summary>
        public static bool HasPending() => PendingCount > 0;

        /// <summary>
        /// Decrypts and ingests all .elog files (compat signature: named arg 'writeDbLog' supported).
        /// - writeDbLog: (utc, type, detail) => true if DB insert succeeded (use your LogRepository).
        /// - deleteFile: path => true if securely deleted.
        /// Files that fail decrypt/parse/insert are quarantined with a suffix reason.
        /// </summary>
        public static void FlushToDb(
            Func<DateTime, EarlyFailType, string, bool> writeDbLog,
            Func<string, bool> deleteFile)
        {
            if (writeDbLog == null) throw new ArgumentNullException(nameof(writeDbLog));
            if (deleteFile == null) throw new ArgumentNullException(nameof(deleteFile));

            EnsureFolders(out var earlyDir, out var quarantineDir);

            foreach (var file in Directory.EnumerateFiles(earlyDir, $"*{FileExt}"))
            {
                try
                {
                    var bytes = File.ReadAllBytes(file);
                    var plain = Unprotect(bytes);

                    var env = JsonSerializer.Deserialize<EarlyLogEnvelope>(plain, JsonOpts);
                    if (env == null)
                        throw new InvalidDataException("Failed to parse EarlyLogEnvelope.");

                    // Combine detail + exception (if present) for payload convenience
                    var detail = env.Detail ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(env.Exception))
                        detail = string.IsNullOrWhiteSpace(detail) ? env.Exception! : $"{detail}\n{env.Exception}";

                    if (writeDbLog(env.CreatedUtc, env.Type, detail))
                    {
                        // Caller performs secure delete
                        if (!deleteFile(file))
                        {
                            // Fallback best-effort secure delete (still okay if your deleter already handled it)
                            SecureDelete(file);
                        }
                    }
                    else
                    {
                        Quarantine(file, quarantineDir, "insert-failed");
                    }
                }
                catch (CryptographicException)
                {
                    Quarantine(file, quarantineDir, "dpapi-decrypt-failed");
                }
                catch (JsonException)
                {
                    Quarantine(file, quarantineDir, "json-parse-failed");
                }
                catch
                {
                    Quarantine(file, quarantineDir, "ingest-exception");
                }
            }
        }

        // ---- Internals --------------------------------------------------------

        private static void EnsureFolders(out string earlyDir, out string quarantineDir)
        {
            var baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName);

            earlyDir = Path.Combine(baseDir, EarlyFolderName);
            quarantineDir = Path.Combine(earlyDir, QuarantineSubdir);

            Directory.CreateDirectory(earlyDir);
            Directory.CreateDirectory(quarantineDir);
        }

        private static byte[] Protect(byte[] data) =>
            ProtectedData.Protect(data, OptionalEntropy, DataProtectionScope.CurrentUser);

        private static byte[] Unprotect(byte[] data) =>
            ProtectedData.Unprotect(data, OptionalEntropy, DataProtectionScope.CurrentUser);

        private static string Sha256Hex(string text)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static void Quarantine(string file, string quarantineDir, string reason)
        {
            try
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var dest = Path.Combine(quarantineDir, $"{name}__{reason}{FileExt}");
                File.Move(file, dest, overwrite: true);
            }
            catch
            {
                // leave it in place to avoid silent loss
            }
        }

        /// <summary>
        /// Best-effort secure delete: overwrite with zeros then delete.
        /// Your caller should supply SensitiveDataCleaner.SecureFileDelete; this is a fallback.
        /// </summary>
        private static void SecureDelete(string path)
        {
            try
            {
                var len = new FileInfo(path).Length;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
                {
                    var zero = new byte[64 * 1024];
                    long remaining = len;
                    while (remaining > 0)
                    {
                        var chunk = (int)Math.Min(remaining, zero.Length);
                        fs.Write(zero, 0, chunk);
                        remaining -= chunk;
                    }
                    fs.Flush(true);
                }
            }
            catch { /* ignore */ }
            try { File.Delete(path); } catch { /* ignore */ }
        }

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        // ---- Envelope DTO -----------------------------------------------------
        private sealed class EarlyLogEnvelope
        {
            public DateTime CreatedUtc { get; set; }
            public EarlyFailType Type { get; set; }
            public string? Detail { get; set; }
            public string? Exception { get; set; }
            public int Version { get; set; } = 1;

            public static EarlyLogEnvelope Create(EarlyFailType type, string detail, Exception? ex)
            {
                return new EarlyLogEnvelope
                {
                    CreatedUtc = DateTime.UtcNow,
                    Type = type,
                    Detail = detail,
                    Exception = ex?.ToString(),
                    Version = 1
                };
            }
        }
    }
}
