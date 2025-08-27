// Utilities/Diagnostics/EarlyLoginFailures.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Utilities.Diagnostics
{
    /// <summary>
    /// Early login failure artifacts:
    ///  - Writer: persistent to %LOCALAPPDATA%\MWPV\early (fallback %TEMP%\MWPV\early)
    ///  - Packet: ASCII header "ELOGP|1\n" + UTF-8 (no BOM) JSON body
    ///  - Ingest: migrate from TEMP -> persistent, validate, dedupe, quarantine, retain
    ///
    /// Back-compat shims retained for existing code:
    ///  - StoreDir (alias to persistent root)
    ///  - EnumeratePendingPaths()
    ///  - Quarantine(string, string) is public
    /// </summary>
    public static class EarlyLoginFailures
    {
        // ===== Packet header (must match reader & writer) =====
        private const string MAGIC = "ELOGP";
        private const byte VERSION = 1;
        private static readonly byte[] HEADER = Encoding.ASCII.GetBytes($"{MAGIC}|{VERSION}\n");

        // ===== Locations =====
        private static readonly string PersistentRoot =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MWPV", "early");

        private static readonly string TempRoot =
            Path.Combine(Path.GetTempPath(), "MWPV", "early");

        private static string QuarantineDir => Path.Combine(PersistentRoot, "quarantine");
        private static string DedupeDir => Path.Combine(PersistentRoot, ".dedupe");

        // ===== Back-compat: legacy expectations from EarlyLogIngestor.cs =====

        /// <summary>
        /// Legacy property used by older code; points to the persistent root.
        /// </summary>
        public static string StoreDir => PersistentRoot;

        /// <summary>
        /// Legacy enumerator of pending files. We return *.elogp from persistent
        /// first (preferred), then temp, newest first.
        /// </summary>
        public static System.Collections.Generic.IEnumerable<string> EnumeratePendingPaths()
        {
            System.Collections.Generic.IEnumerable<string> all = Enumerable.Empty<string>();

            try
            {
                if (Directory.Exists(PersistentRoot))
                    all = all.Concat(Directory.EnumerateFiles(PersistentRoot, "*.elogp", SearchOption.TopDirectoryOnly));
            }
            catch { /* ignore */ }

            try
            {
                if (Directory.Exists(TempRoot))
                    all = all.Concat(Directory.EnumerateFiles(TempRoot, "*.elogp", SearchOption.TopDirectoryOnly));
            }
            catch { /* ignore */ }

            return all.Select(p =>
            {
                try { return (path: p, t: new FileInfo(p).LastWriteTimeUtc); }
                catch { return (path: p, t: DateTime.MinValue); }
            })
                   .OrderByDescending(x => x.t)
                   .Select(x => x.path);
        }

        // ===== Public surface (compat preserved) =====

        /// <summary>Probe at startup; checks both persistent and temp roots.</summary>
        public static bool HasPending()
        {
            try
            {
                if (Directory.Exists(PersistentRoot) &&
                    Directory.EnumerateFiles(PersistentRoot, "*.elogp", SearchOption.TopDirectoryOnly).Any())
                    return true;

                if (Directory.Exists(TempRoot) &&
                    Directory.EnumerateFiles(TempRoot, "*.elogp", SearchOption.TopDirectoryOnly).Any())
                    return true;
            }
            catch { /* ignore */ }
            return false;
        }

        // Overloads retained for call-site compatibility
        public static void Record(EarlyFailType type, string message) =>
            Record(type, message, details: null);

        public static void Record(EarlyFailType type, string message, Exception ex) =>
            Record(type, message, new { exception = ex.GetType().FullName, ex.Message, ex.StackTrace });

        public static void Record(EarlyFailType type, string message, object? details)
        {
            var typeToken = TypeToToken(type);
            var dto = new
            {
                whenUtc = DateTime.UtcNow,
                type = typeToken,
                message = message ?? string.Empty,
                details,
                // lightweight context (no special PII beyond standard env info)
                machine = Environment.MachineName,
                user = Environment.UserName,
                pid = Environment.ProcessId
            };

            var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            var body = utf8NoBom.GetBytes(JsonSerializer.Serialize(dto));

            var fileName = $"{DateTime.UtcNow:yyyyMMddTHHmmssfff}_{San(typeToken)}_{Guid.NewGuid():N}.elogp";
            if (!TryWritePacketAtomically(PersistentRoot, fileName, body))
                TryWritePacketAtomically(TempRoot, fileName, body); // fallback
        }

        /// <summary>
        /// Call once during app startup (after secure store/logging is initialized).
        /// Migrates from TEMP to persistent, ingests all packets, quarantines bad ones,
        /// de-duplicates, and applies retention.
        /// </summary>
        public static void IngestOnStartup(int retentionDays = 30)
        {
            try
            {
                Directory.CreateDirectory(PersistentRoot);
                Directory.CreateDirectory(QuarantineDir);
                Directory.CreateDirectory(DedupeDir);

                // 1) Migrate from TEMP to persistent (preserve evidence)
                MigrateTempToPersistent();

                // 2) Ingest (top-level only)
                var files = Directory.EnumerateFiles(PersistentRoot, "*.elogp", SearchOption.TopDirectoryOnly)
                                     .OrderBy(f => f) // oldest first
                                     .ToArray();

                int ok = 0, dup = 0, bad = 0;
                foreach (var f in files)
                {
                    try
                    {
                        if (!ValidateHeader(f, out int headerLen))
                        {
                            Quarantine(f, "bad_header");
                            bad++;
                            continue;
                        }

                        byte[] body;
                        using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            body = new byte[fs.Length - headerLen];
                            fs.Position = headerLen;
                            int read = fs.Read(body, 0, body.Length);
                            if (read != body.Length) { Quarantine(f, "short_read"); bad++; continue; }
                        }

                        var hash = Sha256Hex(body);
                        if (IsDuplicate(hash))
                        {
                            TryDelete(f);
                            dup++;
                            continue;
                        }

                        // Validate JSON structure
                        try { _ = JsonDocument.Parse(body); }
                        catch { Quarantine(f, "bad_json"); bad++; continue; }

                        // Mark as ingested and delete packet
                        Remember(hash);
                        TryDelete(f);
                        ok++;
                    }
                    catch (Exception ex)
                    {
                        Quarantine(f, "ingest_exception_" + ex.GetType().Name);
                        bad++;
                    }
                }

                Debug.WriteLine($"[EARLY_INGEST] summary ok={ok} dup={dup} bad={bad}");

                // 3) Retention window for persistent & quarantine
                PurgeOld(PersistentRoot, retentionDays);
                PurgeOld(QuarantineDir, retentionDays);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EARLY_INGEST] fatal:{ex.GetType().Name}:{ex.Message}");
            }
        }

        // ===== Helpers =====

        private static string TypeToToken(EarlyFailType t) => t switch
        {
            EarlyFailType.InvalidPasswordOrKeyFile => "invalid-password-or-key-file",
            EarlyFailType.KeyFileVerifyError => "key-file-verify-error",
            EarlyFailType.ArchiveOpenError => "archive-open-error",
            EarlyFailType.KeyArchiveWriteError => "key-archive-write-error",
            EarlyFailType.UnexpectedException => "unexpected-exception",
            _ => "early-failure"
        };

        private static bool TryWritePacketAtomically(string root, string fileName, byte[] body)
        {
            try
            {
                Directory.CreateDirectory(root);
                var path = Path.Combine(root, fileName);
                var tmp = path + ".part";

                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    fs.Write(HEADER, 0, HEADER.Length);
                    fs.Write(body, 0, body.Length);
                    fs.Flush(true);
                }
                MoveOverwrite(tmp, path);
                return true;
            }
            catch { return false; }
        }

        private static void MigrateTempToPersistent()
        {
            try
            {
                if (!Directory.Exists(TempRoot)) return;
                foreach (var f in Directory.EnumerateFiles(TempRoot, "*.elogp", SearchOption.TopDirectoryOnly))
                {
                    var dest = Path.Combine(PersistentRoot, Path.GetFileName(f));
                    try { MoveOverwrite(f, dest); } catch { /* best-effort */ }
                }
            }
            catch { /* ignore */ }
        }

        private static bool ValidateHeader(string file, out int headerLen)
        {
            headerLen = HEADER.Length;
            try
            {
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (fs.Length < headerLen) return false;
                var head = new byte[headerLen];
                var read = fs.Read(head, 0, head.Length);
                if (read != head.Length) return false;
                return head.SequenceEqual(HEADER);
            }
            catch { return false; }
        }

        /// <summary>
        /// Public for legacy codepaths that call into EarlyLoginFailures.Quarantine(...).
        /// </summary>
        public static void Quarantine(string file, string reason)
        {
            try
            {
                Directory.CreateDirectory(QuarantineDir);
                var name = Path.GetFileNameWithoutExtension(file);
                var dest = Path.Combine(QuarantineDir, $"{name}_{San(reason)}.elogp");
                MoveOverwrite(file, dest);
                Debug.WriteLine($"[EARLY_INGEST] Quarantine '{dest}' :: {reason}");
            }
            catch { /* ignore */ }
        }

        // Convenience overload used by some older callsites
        public static void Quarantine(string file) => Quarantine(file, "unspecified");

        private static void PurgeOld(string root, int days)
        {
            try
            {
                if (!Directory.Exists(root)) return;
                var cutoff = DateTime.UtcNow.AddDays(-days);
                foreach (var f in Directory.EnumerateFiles(root, "*.elogp", SearchOption.AllDirectories))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(f) < cutoff)
                            File.Delete(f);
                    }
                    catch { /* ignore */ }
                }
            }
            catch { /* ignore */ }
        }

        private static bool IsDuplicate(string sha256Hex)
        {
            try
            {
                Directory.CreateDirectory(DedupeDir);
                var token = Path.Combine(DedupeDir, sha256Hex + ".seen");
                return File.Exists(token);
            }
            catch { return false; }
        }

        private static void Remember(string sha256Hex)
        {
            try
            {
                Directory.CreateDirectory(DedupeDir);
                var token = Path.Combine(DedupeDir, sha256Hex + ".seen");
                using (File.Create(token)) { }
            }
            catch { /* ignore */ }
        }

        private static string Sha256Hex(byte[] data)
        {
            try
            {
                using var sha = SHA256.Create();
                return Convert.ToHexString(sha.ComputeHash(data)).ToLowerInvariant();
            }
            catch { return "0"; }
        }

        private static void TryDelete(string file)
        {
            try { File.Delete(file); } catch { /* ignore */ }
        }

        private static string San(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "x";
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Length > 80 ? s[..80] : s;
        }

        private static void MoveOverwrite(string src, string dst)
        {
            try
            {
#if NET6_0_OR_GREATER
                File.Move(src, dst, overwrite: true);
#else
                if (File.Exists(dst)) File.Delete(dst);
                File.Move(src, dst);
#endif
            }
            catch
            {
                // As a last resort, try copy+delete
                try
                {
                    if (File.Exists(dst)) File.Delete(dst);
                    File.Copy(src, dst, overwrite: true);
                    File.Delete(src);
                }
                catch { /* ignore */ }
            }
        }
    }

    // Keep your enum wherever it lives; shown here for context only.
    // public enum EarlyFailType { InvalidPasswordOrKeyFile, KeyFileVerifyError, ArchiveOpenError, KeyArchiveWriteError, UnexpectedException }
}
