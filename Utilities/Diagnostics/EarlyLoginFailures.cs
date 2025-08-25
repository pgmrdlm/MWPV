// MWPV/Utilities/Diagnostics/EarlyLoginFailures.cs
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Collections.Generic;
using System.Linq; // <-- remove if you prefer a non-LINQ HasPending()
using Utilities.Security; // SecureEncryptedDataStore

namespace Utilities.Diagnostics
{
    /// <summary>
    /// Writes DPAPI-encrypted "early" login failure artifacts to %TEMP%\MWPV\early.
    /// Files are .elogp and contain an encrypted JSON body (no plaintext).
    /// Provides compatibility shims for legacy callers: HasPending() and Record(...).
    /// </summary>
    public static class EarlyLoginFailures
    {
        // -------- Locations --------
        public static string StoreDir
            => Path.Combine(Path.GetTempPath(), "MWPV", "early");

        public static string QuarantineDir
            => Path.Combine(StoreDir, "quarantine");

        private static void EnsureDirs()
        {
            try { Directory.CreateDirectory(StoreDir); } catch { /* best-effort */ }
            try { Directory.CreateDirectory(QuarantineDir); } catch { /* best-effort */ }
        }

        // -------- Compatibility shims (keep existing call sites building) --------

        /// <summary>Legacy probe used in App.xaml.cs to see if any early files exist.</summary>
        public static bool HasPending()
        {
            try
            {
                if (!Directory.Exists(StoreDir)) return false;
                // Prefer encrypted .elogp, but also detect stray legacy .elog
                return Directory.EnumerateFiles(StoreDir, "*.elogp").Any()
                    || Directory.EnumerateFiles(StoreDir, "*.elog").Any();

                // Non-LINQ alternative:
                // foreach (var _ in Directory.EnumerateFiles(StoreDir, "*.elogp")) return true;
                // foreach (var _ in Directory.EnumerateFiles(StoreDir, "*.elog"))  return true;
                // return false;
            }
            catch { return false; }
        }

        /// <summary>Legacy generic record (message-only).</summary>
        public static void Record(string type, string message)
            => WriteGeneric(type, message, details: null);

        /// <summary>Legacy generic record with exception.</summary>
        public static void Record(string type, string message, Exception ex)
            => WriteGeneric(type, message, new { exception = ex.GetType().FullName, ex.Message, ex.StackTrace });

        /// <summary>Legacy generic record with object details.</summary>
        public static void Record(string type, string message, object details)
            => WriteGeneric(type, message, details);
        // --- Back-compat: enum-based API -------------------------------------------
        public static void Record(EarlyFailType type, string message)
            => Record(ToTypeString(type), message);

        public static void Record(EarlyFailType type, string message, Exception ex)
            => Record(ToTypeString(type), message, ex);

        public static void Record(EarlyFailType type, string message, object details)
            => Record(ToTypeString(type), message, details);

        // Optional convenience, mirrors WriteInvalidPasswordOrKeyFile pattern
        public static void Write(EarlyFailType type, string message, object? details = null)
            => WriteGeneric(ToTypeString(type), message, details);

        // Map the legacy enum to our canonical string type names
        private static string ToTypeString(EarlyFailType t) => t switch
        {
            EarlyFailType.InvalidPasswordOrKeyFile => "invalid-password-or-key-file",
            EarlyFailType.ArchiveOpenError => "archive-open-error",
            EarlyFailType.KeyArchiveWriteError => "key-archive-write-error",
            EarlyFailType.UnexpectedException => "unexpected-exception",
            // fallback for anything else / unknown
            _ => "early-failure"
        };

        // -------- Specific helpers you can call directly --------

        public static void WriteInvalidPasswordOrKeyFile(string keyFilePath, string message, Exception? ex = null)
        {
            var dto = MakeBaseDto(
                type: "invalid-password-or-key-file",
                message: message,
                details: ex == null
                    ? $"Invalid key-file password or unsupported/unencrypted archive. Path='{keyFilePath}'"
                    : new
                    {
                        error = "Invalid key-file password or unsupported/unencrypted archive",
                        keyFilePath,
                        exception = ex.GetType().FullName,
                        ex.Message,
                        ex.StackTrace
                    });

            WriteEncrypted(dto, FileNamePrefix("invalid-password-or-key-file"));
        }

        public static void WriteGeneric(string type, string message, object? details = null)
        {
            var dto = MakeBaseDto(type, message, details);
            WriteEncrypted(dto, FileNamePrefix(type));
        }

        // -------- File discovery / quarantine --------

        /// <summary>Enumerate pending early files (encrypted first, then legacy plaintext) newest first.</summary>
        public static IEnumerable<string> EnumeratePendingPaths()
        {
            EnsureDirs();

            IEnumerable<string> enc = Array.Empty<string>();
            IEnumerable<string> plain = Array.Empty<string>();
            try { enc = Directory.EnumerateFiles(StoreDir, "*.elogp"); } catch { }
            try { plain = Directory.EnumerateFiles(StoreDir, "*.elog"); } catch { }

            static DateTime Touch(string p) => new FileInfo(p).LastWriteTimeUtc;

            foreach (var p in SortByNewest(enc)) yield return p;
            foreach (var p in SortByNewest(plain)) yield return p;

            static IEnumerable<string> SortByNewest(IEnumerable<string> files)
            {
                List<(string path, DateTime t)> list = new();
                foreach (var f in files)
                {
                    try { list.Add((f, Touch(f))); } catch { list.Add((f, DateTime.MinValue)); }
                }
                list.Sort((a, b) => b.t.CompareTo(a.t));
                foreach (var (path, _) in list) yield return path;
            }
        }

        /// <summary>Move a problematic file into quarantine with an optional reason suffix.</summary>
        public static void Quarantine(string path, string? why = null)
        {
            try
            {
                EnsureDirs();
                var name = Path.GetFileName(path);
                var stamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff");
                var reason = string.IsNullOrWhiteSpace(why) ? "" : $"_{SanitizeFileToken(why)}";
                var dest = Path.Combine(QuarantineDir, $"{stamp}_{name}{reason}");
                File.Move(path, dest, overwrite: true);
            }
            catch { /* best-effort */ }
        }

        // -------- Internals --------

        private static object MakeBaseDto(string type, string message, object? details)
        {
            string userSid = "UnknownUser";
            string userName = Environment.UserName;
            try { userSid = WindowsIdentity.GetCurrent()?.User?.Value ?? userSid; } catch { }

            return new
            {
                version = 1,
                utc = DateTime.UtcNow.ToString("o"),
                type = type ?? "unknown",
                sessionId = AppSessionId, // best-effort session id (short hash)
                machine = Environment.MachineName,
                userSid,
                userName,
                appVersion = "unknown", // you can plumb your version in if you want
                message = message ?? "",
                details
            };
        }

        private static void WriteEncrypted(object dto, string filePrefix)
        {
            EnsureDirs();

            // Serialize compact JSON
            var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = false });
            var bytes = Encoding.UTF8.GetBytes(json);

            // Encrypt with DPAPI (LocalMachine) + optional app entropy
            byte[] entropy = AcquireEntropy();
            byte[] cipher = Array.Empty<byte>();
            try
            {
                cipher = ProtectedData.Protect(bytes, entropy.Length == 0 ? null : entropy, DataProtectionScope.LocalMachine);
            }
            finally
            {
                // plaintext wipe (best-effort)
                Array.Clear(bytes, 0, bytes.Length);
            }

            // Write atomically to .elogp
            string name = $"{filePrefix}_{ShortHash(json)}.elogp";
            string finalPath = Path.Combine(StoreDir, name);
            string tempPath = finalPath + ".tmp";

            try
            {
                using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    fs.Write(cipher, 0, cipher.Length);
                    fs.Flush(true);
                }
                File.Move(tempPath, finalPath, overwrite: true);
            }
            catch
            {
                // Cleanup temp if we failed
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                throw;
            }
            finally
            {
                Array.Clear(cipher, 0, cipher.Length);
                if (entropy.Length > 0) Array.Clear(entropy, 0, entropy.Length);
            }
        }

        private static string FileNamePrefix(string type)
        {
            var t = string.IsNullOrWhiteSpace(type) ? "event" : SanitizeFileToken(type);
            return $"{DateTime.UtcNow:yyyyMMddTHHmmssfff}_{t}";
        }

        private static string SanitizeFileToken(string s)
        {
            s ??= "";
            foreach (var ch in Path.GetInvalidFileNameChars())
                s = s.Replace(ch, '_');
            // keep it short
            return s.Length > 64 ? s[..64] : s;
        }

        private static string ShortHash(string text)
        {
            try
            {
                using var sha = SHA256.Create();
                var h = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""));
                // 12 hex chars is enough for a short collision-resistant suffix
                return Convert.ToHexString(h).ToLowerInvariant()[..12];
            }
            catch { return "000000000000"; }
        }

        /// <summary>
        /// Get or create extra-entropy bytes for DPAPI from SEDS ("Key.TempFiles").
        /// Best-effort: if SEDS is unavailable, returns empty array (still secure because DPAPI is machine-bound).
        /// </summary>
        private static byte[] AcquireEntropy()
        {
            try
            {
                const string K = "Key.TempFiles";
                if (SecureEncryptedDataStore.HasKey(K))
                {
                    var b = SecureEncryptedDataStore.GetBytes(K);
                    return (b != null && b.Length > 0) ? b : Array.Empty<byte>();
                }

                // Create once, best-effort
                var newKey = new byte[32];
                RandomNumberGenerator.Fill(newKey);
                try
                {
                    SecureEncryptedDataStore.Set(K, newKey);
                    // Return a copy; keep store as the source of truth
                    var copy = new byte[newKey.Length];
                    Buffer.BlockCopy(newKey, 0, copy, 0, newKey.Length);
                    Array.Clear(newKey, 0, newKey.Length);
                    return copy;
                }
                catch
                {
                    // Couldn't persist — fall back to no entropy
                    Array.Clear(newKey, 0, newKey.Length);
                    return Array.Empty<byte>();
                }
            }
            catch
            {
                return Array.Empty<byte>();
            }
        }

        // Optional (future): a decrypt helper for the ingestor if you want to keep responsibilities together.
        // public static byte[] TryDecrypt(byte[] blob)
        // {
        //     try
        //     {
        //         var entropy = AcquireEntropy();
        //         var pt = ProtectedData.Unprotect(blob, entropy.Length == 0 ? null : entropy, DataProtectionScope.LocalMachine);
        //         if (entropy.Length > 0) Array.Clear(entropy, 0, entropy.Length);
        //         return pt;
        //     }
        //     catch { return Array.Empty<byte>(); }
        // }

        private static string AppSessionId
        {
            get
            {
                // Short, stable-for-process session id
                try
                {
                    using var sha = SHA256.Create();
                    var seed = $"{Environment.MachineName}|{Environment.ProcessId}|{AppDomain.CurrentDomain.Id}|{DateTime.UtcNow.Date:yyyyMMdd}";
                    return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(seed))).ToLowerInvariant()[..32];
                }
                catch { return "session"; }
            }
        }
    }
}
