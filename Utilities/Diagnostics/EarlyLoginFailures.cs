using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Utilities.Security; // SensitiveDataCleaner

namespace Utilities.Diagnostics
{
    public enum EarlyFailType
    {
        InvalidKeyfilePassword,
        KeyfileMissingOrCorrupt,

        // Added to match existing emitters in SetupPasswordAndKeyFile
        InvalidPasswordOrKeyFile,
        KeyFileVerifyError
    }

    /// <summary>
    /// Writes small, DPAPI-protected ".elog" files before the DB is available, then
    /// ingests them into the DB after a successful login.
    /// - v1 payload includes: Version, Type, Utc, Detail, Guid, ContentHash, Machine, ProcessId
    /// - Backward compatible: reads legacy payload { Type, Utc, Detail }.
    /// </summary>
    internal static partial class EarlyLoginFailures
    {
        // Set this at app startup if you want a custom path. Default: %LOCALAPPDATA%\MWPV\early
        public static string StoreDir { get; set; }

        // ----- Constants / JSON options -----

        private const int CurrentVersion = 1;

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        // ----- Paths -----

        private static string ResolveDir()
        {
            if (!string.IsNullOrWhiteSpace(StoreDir)) return StoreDir;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MWPV", "early");
        }

        private static string EnsureDir()
        {
            var dir = ResolveDir();
            Directory.CreateDirectory(dir);
            return dir;
        }

        // ----- Public API: Write -----

        public static void Record(EarlyFailType type, string detail = null)
        {
            var dir = EnsureDir();

            var utc = DateTime.UtcNow;
            string guid = Guid.NewGuid().ToString("N");

            // Build v1 payload
            var v1 = new ElogV1
            {
                Version = CurrentVersion,
                Type = type.ToString(),
                Utc = utc,
                Detail = detail ?? string.Empty,
                Guid = guid,
                Machine = Environment.MachineName,
                ProcessId = Environment.ProcessId
            };

            // Compute content hash (type + detail) — version/utc/guid excluded on purpose
            v1.ContentHash = ComputeHash($"{v1.Type}\n{v1.Detail}");

            var json = JsonSerializer.Serialize(v1, JsonOpts);
            var bytes = Encoding.UTF8.GetBytes(json);

            var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);

            // Include type + guid in name to help quick triage; timestamp sorts lexically
            var name = $"{utc:yyyyMMddHHmmssfff}-{v1.Type}-{v1.Guid}.elog";
            File.WriteAllBytes(Path.Combine(dir, name), protectedBytes);
        }

        public static bool HasPending()
        {
            var dir = ResolveDir();
            return Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*.elog").Any();
        }

        public static int PendingCount()
        {
            var dir = ResolveDir();
            return Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*.elog").Count() : 0;
        }

        /// <summary>
        /// After a successful DB login: write a normal DB log entry for each pending file, then securely delete.
        /// Returns true if at least one item was successfully written to the DB.
        /// </summary>
        /// <param name="writeDbLog">(utc, type, detail) => true if written; throws or false to skip delete.</param>
        /// <param name="secureFileDelete">
        /// Optional: custom delete delegate. If null, uses SensitiveDataCleaner.QuarantineThenSecureDelete with an internal quarantine dir.
        /// </param>
        public static bool FlushToDb(Func<DateTime, EarlyFailType, string, bool> writeDbLog,
                                     Func<string, bool> secureFileDelete = null)
        {
            if (writeDbLog == null) throw new ArgumentNullException(nameof(writeDbLog));

            var dir = EnsureDir();
            var files = Directory.EnumerateFiles(dir, "*.elog").OrderBy(f => f).ToArray();
            if (files.Length == 0) return false;

            bool anyWritten = false;
            string quarantineDir = Path.Combine(dir, "quarantine");
            Directory.CreateDirectory(quarantineDir);

            // In-batch dedupe guards (best-effort)
            var seenGuids = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenHashes = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                // 1) Read with exclusive handle so nothing else can touch it mid-ingest
                byte[] prot;
                try
                {
                    using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                        prot = new byte[fs.Length];
                        int off = 0, read;
                        while (off < prot.Length && (read = fs.Read(prot, off, prot.Length - off)) > 0) off += read;
                        if (off != prot.Length) throw new EndOfStreamException("Unexpected EOF reading .elog");
                    }
                }
                catch (Exception ex)
                {
                    // Couldn’t read — keep file for next time
                    System.Diagnostics.Debug.WriteLine($"[EarlyLoginFailures] READ FAILED {Path.GetFileName(file)}: {ex.Message}");
                    continue;
                }

                // 2) Decrypt + parse (v1 preferred; fallback to legacy)
                DateTime utc = DateTime.UtcNow;
                EarlyFailType type = EarlyFailType.KeyfileMissingOrCorrupt;
                string detail = string.Empty;

                try
                {
                    var plain = ProtectedData.Unprotect(prot, null, DataProtectionScope.CurrentUser);
                    var json = Encoding.UTF8.GetString(plain);

                    if (TryParseV1(json, out var v1))
                    {
                        utc = v1.Utc;
                        type = ParseType(v1.Type);
                        detail = v1.Detail ?? string.Empty;

                        // In-batch dedupe: prefer GUID; else hash
                        if (!string.IsNullOrWhiteSpace(v1.Guid) && !seenGuids.Add(v1.Guid))
                        {
                            // duplicate in same run — just delete this duplicate
                            TrySecureDeleteWithFallbacks(file, secureFileDelete, quarantineDir);
                            continue;
                        }
                        if (!string.IsNullOrWhiteSpace(v1.ContentHash) && !seenHashes.Add(v1.ContentHash))
                        {
                            TrySecureDeleteWithFallbacks(file, secureFileDelete, quarantineDir);
                            continue;
                        }
                    }
                    else if (TryParseLegacy(json, out var legacy))
                    {
                        utc = legacy.Utc;
                        type = ParseType(legacy.Type);
                        detail = legacy.Detail ?? string.Empty;

                        // Legacy: make a best-effort dedupe via hash of type+detail
                        var legacyHash = ComputeHash($"{legacy.Type}\n{legacy.Detail}");
                        if (!seenHashes.Add(legacyHash))
                        {
                            TrySecureDeleteWithFallbacks(file, secureFileDelete, quarantineDir);
                            continue;
                        }
                    }
                    else
                    {
                        // Unknown format: attempt to remove so we don't loop forever
                        System.Diagnostics.Debug.WriteLine($"[EarlyLoginFailures] UNKNOWN FORMAT {Path.GetFileName(file)}");
                        TrySecureDeleteWithFallbacks(file, secureFileDelete, quarantineDir);
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    // Corrupt/unreadable — attempt to remove so we don't loop forever
                    System.Diagnostics.Debug.WriteLine($"[EarlyLoginFailures] DECRYPT/PARSE FAILED {Path.GetFileName(file)}: {ex.Message}");
                    TrySecureDeleteWithFallbacks(file, secureFileDelete, quarantineDir);
                    continue;
                }

                // 3) Attempt DB write
                bool wrote = false;
                try
                {
                    wrote = writeDbLog(utc, type, detail);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[EarlyLoginFailures] DB WRITE THREW for {Path.GetFileName(file)}: {ex.Message}");
                    wrote = false;
                }

                if (!wrote)
                {
                    // Leave file for next session
                    System.Diagnostics.Debug.WriteLine($"[EarlyLoginFailures] SKIP DELETE; writeDbLog=false for {Path.GetFileName(file)}");
                    continue;
                }

                anyWritten = true;

                // 4) Securely delete via quarantine (break races/locks) with diagnostics
                TrySecureDeleteWithFallbacks(file, secureFileDelete, quarantineDir);
            }

            // Optional hygiene: if none pending, sweep quarantine + early dir
            if (PendingCount() == 0)
            {
                SensitiveDataCleaner.SecureDeleteAllFiles(quarantineDir, overwritePasses: 1);
                SensitiveDataCleaner.SecureDeleteAllFiles(dir, overwritePasses: 1);
            }

            return anyWritten;
        }

        // ----- Helpers -----

        /// <summary>
        /// Try caller's secure delete; if not provided, use QuarantineThenSecureDelete.
        /// If still present, clear attrs + hard delete; as last resort, rename + delete.
        /// </summary>
        private static void TrySecureDeleteWithFallbacks(string file, Func<string, bool> secureFileDelete, string quarantineDir)
        {
            bool deleted = false;

            try
            {
                if (secureFileDelete != null)
                {
                    deleted = secureFileDelete(file);
                }
                else
                {
                    var del = SensitiveDataCleaner.QuarantineThenSecureDelete(file, quarantineDir, overwritePasses: 1, maxRetries: 5);
                    deleted = del.Success;
                }

                if (!deleted && File.Exists(file))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                    deleted = !File.Exists(file);
                }

                if (!deleted && File.Exists(file))
                {
                    string tmp = file + ".del." + Guid.NewGuid().ToString("N");
                    File.Move(file, tmp);
                    File.Delete(tmp);
                    deleted = !File.Exists(tmp) && !File.Exists(file);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EarlyLoginFailures] DELETE FALLBACK FAILED {Path.GetFileName(file)}: {ex.Message}");
            }

            if (!deleted && File.Exists(file))
            {
                System.Diagnostics.Debug.WriteLine($"[EarlyLoginFailures] WARNING: Could not delete {Path.GetFileName(file)} after ingestion.");
            }
        }

        private static EarlyFailType ParseType(string type)
        {
            if (Enum.TryParse<EarlyFailType>(type, ignoreCase: true, out var t))
                return t;

            // Map common string emitters if they don't match enum naming exactly
            if (string.Equals(type, "InvalidPasswordOrKeyFile", StringComparison.OrdinalIgnoreCase))
                return EarlyFailType.InvalidPasswordOrKeyFile;
            if (string.Equals(type, "KeyFileVerifyError", StringComparison.OrdinalIgnoreCase))
                return EarlyFailType.KeyFileVerifyError;

            return EarlyFailType.KeyfileMissingOrCorrupt;
        }

        private static string ComputeHash(string s)
        {
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(s)));
        }

        // ----- Payloads -----

        private sealed class ElogV1
        {
            public int Version { get; set; }
            public string Type { get; set; }
            public DateTime Utc { get; set; }
            public string Detail { get; set; }
            public string Guid { get; set; }
            public string ContentHash { get; set; }
            public string Machine { get; set; }
            public int ProcessId { get; set; }
        }

        private sealed class Legacy
        {
            public string Type { get; set; }
            public DateTime Utc { get; set; }
            public string Detail { get; set; }
        }

        private static bool TryParseV1(string json, out ElogV1 v1)
        {
            v1 = null;
            try
            {
                // Quick probe: must contain "version":1
                if (!json.Contains("\"version\":", StringComparison.OrdinalIgnoreCase))
                    return false;

                var parsed = JsonSerializer.Deserialize<ElogV1>(json, JsonOpts);
                if (parsed == null || parsed.Version != 1) return false;
                v1 = parsed;
                return true;
            }
            catch { return false; }
        }

        private static bool TryParseLegacy(string json, out Legacy legacy)
        {
            legacy = null;
            try
            {
                var parsed = JsonSerializer.Deserialize<Legacy>(json, JsonOpts);
                if (parsed == null || string.IsNullOrWhiteSpace(parsed.Type)) return false;
                legacy = parsed;
                return true;
            }
            catch { return false; }
        }
    }

    /// <summary>
    /// Overload shim so we can call Record("stringType", "detail") directly.
    /// Converts to EarlyFailType if possible; otherwise uses KeyfileMissingOrCorrupt as fallback.
    /// </summary>
    internal static partial class EarlyLoginFailures
    {
        public static void Record(string type, string detail)
        {
            if (Enum.TryParse<EarlyFailType>(type, ignoreCase: true, out var parsed))
                Record(parsed, detail);
            else
                Record(EarlyFailType.KeyfileMissingOrCorrupt, $"{type}: {detail}");
        }
    }
}
