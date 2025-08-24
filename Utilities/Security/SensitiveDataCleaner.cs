using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Utilities.Security
{
    /// <summary>
    /// Secure file/dir deletion utilities used by early-ingest cleanup and quarantine maintenance.
    /// - Overwrites file bytes (default 1 pass of zeros), optional random rename, then delete.
    /// - Best-effort: never throws by default; opt-in via throwOnError.
    /// - Includes helpers to sweep the Early folder and to purge old items from Quarantine.
    /// - **Back-compat shims** included to match older call sites.
    /// </summary>
    public static class SensitiveDataCleaner
    {
        // Central paths (align with EarlyLoginFailures)
        public static readonly string EarlyDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MWPV", "early");
        public static readonly string QuarantineDir = Path.Combine(EarlyDir, "quarantine");

        /// <summary>
        /// Securely delete a file: overwrite (N passes), optional rename to random name, then delete.
        /// </summary>
        public static bool SecureDeleteFile(string path, int passes = 1, bool shredName = true, bool throwOnError = false)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists) return true; // already gone
                try { fi.IsReadOnly = false; } catch { }

                long len = fi.Length;
                if (len > 0)
                {
                    OverwriteFile(fi.FullName, len, passes, useRandom: false);
                }

                // Optional rename to random to hide original name
                if (shredName)
                {
                    try
                    {
                        string rndName = Guid.NewGuid().ToString("N") + fi.Extension;
                        string dest = Path.Combine(fi.DirectoryName!, rndName);
                        File.Move(fi.FullName, dest, overwrite: true);
                        fi = new FileInfo(dest);
                    }
                    catch { }
                }

                File.Delete(fi.FullName);
                return !File.Exists(fi.FullName);
            }
            catch
            {
                if (throwOnError) throw;
                return false;
            }
        }

        /// <summary>
        /// Securely delete all files matching pattern in a directory. Returns (total, deleted) counts.
        /// Named param kept as 'overwritePasses' for back-compat.
        /// </summary>
        public static (int total, int deleted) SecureDeleteDirectory(string dir, string searchPattern = "*", SearchOption option = SearchOption.TopDirectoryOnly, int overwritePasses = 1, bool shredNames = true, bool finalZeroPass = true, bool removeDirectories = false)
        {
            int total = 0, deleted = 0;
            try
            {
                if (!Directory.Exists(dir)) return (0, 0);
                foreach (var file in Directory.EnumerateFiles(dir, searchPattern, option))
                {
                    total++;
                    if (SecureDeleteFile(file, overwritePasses, shredName: shredNames)) deleted++;
                }
                return (total, deleted);
            }
            catch { return (total, deleted); }
            finally
            {
                if (removeDirectories)
                {
                    try
                    {
                        // Remove empty subdirectories (or all, recursively) according to the search option
                        foreach (var d in Directory.EnumerateDirectories(dir, "*", option))
                        {
                            try { Directory.Delete(d, recursive: true); } catch { }
                        }
                    }
                    catch { }
                }
            }
        }

        /// <summary>
        /// Back-compat shim used around the app. Deletes all files in a folder (top-level) using secure delete.
        /// </summary>
        public static (int total, int deleted) SecureDeleteAllFiles(string dir, int overwritePasses = 1, bool shredNames = true, bool finalZeroPass = true, bool removeDirectories = false)
            => SecureDeleteDirectory(dir, "*", SearchOption.TopDirectoryOnly, overwritePasses, shredNames, finalZeroPass, removeDirectories);

        // Back-compat overload used by older call sites that omit pattern/option but use named args
        public static (int total, int deleted) SecureDeleteDirectory(string dir, int overwritePasses = 1, bool shredNames = true, bool finalZeroPass = true)
            => SecureDeleteDirectory(dir, "*", SearchOption.TopDirectoryOnly, overwritePasses, shredNames, finalZeroPass);

        /// <summary>
        /// After successful ingest (PendingCount == 0), sweep %LOCALAPPDATA%/MWPV/early of residual *.elog.
        /// Quarantine directory is excluded.
        /// </summary>
        public static (int total, int deleted) SweepEarlyResiduals()
        {
            try
            {
                if (!Directory.Exists(EarlyDir)) return (0, 0);
                int total = 0, deleted = 0;
                foreach (var file in Directory.EnumerateFiles(EarlyDir, "*.elog", SearchOption.TopDirectoryOnly))
                {
                    total++;
                    if (SecureDeleteFile(file)) deleted++;
                }
                return (total, deleted);
            }
            catch { return (0, 0); }
        }

        /// <summary>
        /// Purge quarantine items older than the provided number of days. Does NOT delete fresh quarantines.
        /// </summary>
        public static (int total, int removed) PurgeQuarantine(int olderThanDays)
        {
            int total = 0, removed = 0;
            try
            {
                if (olderThanDays < 1) return (0, 0);
                if (!Directory.Exists(QuarantineDir)) return (0, 0);
                var cutoff = DateTime.UtcNow.AddDays(-olderThanDays);

                foreach (var file in Directory.EnumerateFiles(QuarantineDir, "*", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        var info = new FileInfo(file);
                        if (!info.Exists) continue;
                        if (info.LastWriteTimeUtc > cutoff) continue;
                        total++;

                        TryDeleteIfExists(file + ".reason.txt");
                        TryDeleteIfExists(file + ".manifest.json");

                        File.Delete(file);
                        if (!File.Exists(file)) removed++;
                    }
                    catch { }
                }
            }
            catch { }
            return (total, removed);
        }

        // ===== Back-compat memory wipe shims =====
        public static void WipeString(string? s)
        {
            try
            {
                if (string.IsNullOrEmpty(s)) return;
                var tmp = s.ToCharArray();
                Array.Clear(tmp, 0, tmp.Length);
            }
            catch { }
        }
        public static void WipeString(ref string? s)
        { try { s = string.Empty; } catch { } }

        public static void WipeCharArray(char[]? buffer)
        { try { if (buffer != null) Array.Clear(buffer, 0, buffer.Length); } catch { } }
        public static void WipeByteArray(ref byte[]? buffer)
        { try { if (buffer != null) { Array.Clear(buffer, 0, buffer.Length); buffer = Array.Empty<byte>(); } } catch { } }
        public static void WipeByteArray(byte[]? buffer)
        { try { if (buffer != null) Array.Clear(buffer, 0, buffer.Length); } catch { } }

        public static void Clear(ref byte[]? buffer)
        { try { if (buffer != null) { Array.Clear(buffer, 0, buffer.Length); buffer = Array.Empty<byte>(); } } catch { } }
        public static void Clear(ref char[]? buffer)
        { try { if (buffer != null) { Array.Clear(buffer, 0, buffer.Length); buffer = Array.Empty<char>(); } } catch { } }
        public static void Clear(ref string? s)
        { try { s = string.Empty; } catch { } }
        public static void Clear(byte[]? buffer)
        { try { if (buffer != null) Array.Clear(buffer, 0, buffer.Length); } catch { } }
        public static void Clear(char[]? buffer)
        { try { if (buffer != null) Array.Clear(buffer, 0, buffer.Length); } catch { } }
        public static void Clear(StringBuilder? sb)
        { try { sb?.Clear(); if (sb != null) sb.Capacity = 0; } catch { } }
        public static void Clear(ref StringBuilder? sb)
        { try { sb?.Clear(); if (sb != null) sb.Capacity = 0; } catch { } }

        // WPF helpers so older call sites that passed controls compile
        public static void Clear(System.Windows.Controls.TextBox? tb)
        { try { if (tb != null) tb.Text = string.Empty; } catch { } }
        public static void Clear(ref System.Windows.Controls.TextBox? tb)
        { try { if (tb != null) tb.Text = string.Empty; } catch { } }
        public static void Clear(System.Windows.Controls.PasswordBox? pb)
        { try { pb?.Clear(); } catch { } }
        public static void Clear(ref System.Windows.Controls.PasswordBox? pb)
        { try { pb?.Clear(); } catch { } }
        public static void Clear(System.Security.SecureString? ss)
        { try { ss?.Clear(); } catch { } }
        public static void Clear(ref System.Security.SecureString? ss)
        { try { ss?.Clear(); } catch { } }

        // ===== Helpers =====
        private static void OverwriteFile(string fullPath, long length, int passes, bool useRandom)
        {
            const int BufferSize = 64 * 1024;
            passes = Math.Max(1, Math.Min(passes, 7));
            using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Write, FileShare.None);
            fs.Position = 0;
            var buffer = new byte[BufferSize];
            for (int pass = 0; pass < passes; pass++)
            {
                if (useRandom) RandomNumberGenerator.Fill(buffer); else Array.Clear(buffer, 0, buffer.Length);
                long remaining = length;
                fs.Position = 0;
                while (remaining > 0)
                {
                    int toWrite = (int)Math.Min(BufferSize, remaining);
                    fs.Write(buffer, 0, toWrite);
                    remaining -= toWrite;
                }
                fs.Flush(true);
            }
        }

        public static void TryDeleteIfExists(string path)
        { try { if (File.Exists(path)) File.Delete(path); } catch { } }

        // Back-compat method alias
        public static bool SecureFileDelete(string path, int overwritePasses = 1, bool shredName = true, bool finalZeroPass = true)
            => SecureDeleteFile(path, overwritePasses, shredName);
    }
}
