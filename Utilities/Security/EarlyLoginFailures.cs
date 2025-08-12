using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace Utilities.Security
{
    public enum EarlyFailType { InvalidKeyfilePassword, KeyfileMissingOrCorrupt }

    internal static partial class EarlyLoginFailures
    {
        // Set this at app startup. E.g., %LOCALAPPDATA%\MWPV\early
        public static string StoreDir { get; set; }

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

        public static void Record(EarlyFailType type, string detail = null)
        {
            var dir = EnsureDir();

            var payload = JsonSerializer.Serialize(new EarlyFail
            {
                Type = type.ToString(),
                Utc = DateTime.UtcNow,
                Detail = detail ?? string.Empty
            });

            var bytes = System.Text.Encoding.UTF8.GetBytes(payload);
            var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            var name = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{type}.elog";

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

                // 2) Decrypt + parse
                EarlyFail evt;
                try
                {
                    var plain = ProtectedData.Unprotect(prot, null, DataProtectionScope.CurrentUser);
                    evt = JsonSerializer.Deserialize<EarlyFail>(System.Text.Encoding.UTF8.GetString(plain));
                }
                catch (Exception ex)
                {
                    // Corrupt/unreadable — attempt to remove so we don't loop forever
                    System.Diagnostics.Debug.WriteLine($"[EarlyLoginFailures] DECRYPT/PARSE FAILED {Path.GetFileName(file)}: {ex.Message}");
                    TrySecureDeleteWithFallbacks(file, secureFileDelete, quarantineDir);
                    continue;
                }

                if (!Enum.TryParse<EarlyFailType>(evt?.Type, out var t))
                    t = EarlyFailType.KeyfileMissingOrCorrupt;

                // 3) Attempt DB write
                bool wrote = false;
                try
                {
                    wrote = writeDbLog(evt.Utc, t, evt.Detail ?? string.Empty);
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

        private sealed class EarlyFail
        {
            public string Type { get; set; }
            public DateTime Utc { get; set; }
            public string Detail { get; set; }
        }
    }

    /// <summary>
    /// Overload shim so we can call Record("stringType", "detail") directly.
    /// Converts to EarlyFailType if possible, otherwise uses KeyfileMissingOrCorrupt as fallback.
    /// </summary>
    internal static partial class EarlyLoginFailures
    {
        public static void Record(string type, string detail)
        {
            if (Enum.TryParse<EarlyFailType>(type, out var parsed))
                Record(parsed, detail);
            else
                Record(EarlyFailType.KeyfileMissingOrCorrupt, $"{type}: {detail}");
        }
    }
}
