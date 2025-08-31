// Utilities/Diagnostics/EarlyLogIngestor.cs — full file
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using MWPV.Services;

namespace Utilities.Diagnostics
{
    /// <summary>
    /// Ingests *.elogp from EarlyLoginFailures.StoreDir into DB, with dedupe & quarantine.
    /// </summary>
    public static class EarlyLogIngestor
    {
        /// <summary>
        /// Enumerate early files, validate+decrypt, dedupe by SHA256(rawJson), insert, and secure-delete.
        /// Quarantines bad/duplicate files with a .reason.txt note.
        /// </summary>
        public static void IngestAll(LogRepository repo)
        {
            var dir = EarlyLoginFailures.StoreDir;
            Directory.CreateDirectory(dir);

            var files = Directory.GetFiles(dir, $"*{EarlyLoginFailures.FileExt}", SearchOption.TopDirectoryOnly);
            if (files.Length == 0) return;

            var seenHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in files)
            {
                try
                {
                    if (!EarlyLoginFailures.TryReadAndDecrypt(path, out var entry, out var reason, out var rawJson, out _))
                    {
                        Quarantine(path, reason ?? "read/decrypt failed");
                        continue;
                    }

                    var hashHex = Sha256Hex(rawJson!);
                    if (!seenHashes.Add(hashHex))
                    {
                        Quarantine(path, "duplicate in same run (hash)");
                        continue;
                    }

                    // Persist (route to repo)
                    repo.InsertEarlyFailureAsync(
                        whenUtc: entry!.whenUtc,
                        category: entry.category,
                        message: entry.message,
                        relatedFile: entry.relatedFile,
                        exType: entry.exType,
                        exMessage: entry.exMessage,
                        exStack: entry.exStack,
                        contentHashHex: hashHex
                    ).GetAwaiter().GetResult();

                    SecureDelete(path);
                }
                catch (Exception ex)
                {
                    Quarantine(path, "ingest exception: " + ex.Message);
                }
            }
        }

        private static string Sha256Hex(byte[] data)
        {
            using var sha = SHA256.Create();
            var b = sha.ComputeHash(data);
            var sb = new StringBuilder(b.Length * 2);
            foreach (var t in b) sb.Append(t.ToString("x2"));
            return sb.ToString();
        }

        private static void SecureDelete(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > 0 && fi.Length <= 1024 * 1024)
                {
                    using var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
                    var zeros = new byte[8192];
                    long remaining = fi.Length;
                    while (remaining > 0)
                    {
                        var w = (int)Math.Min(zeros.Length, remaining);
                        fs.Write(zeros, 0, w);
                        remaining -= w;
                    }
                    fs.Flush(true);
                }
            }
            catch { }
            finally
            {
                try { File.Delete(path); } catch { }
            }
        }

        private static void Quarantine(string path, string? reason)
        {
            try
            {
                Directory.CreateDirectory(EarlyLoginFailures.QuarantineDir);
                var dest = Path.Combine(EarlyLoginFailures.QuarantineDir, Path.GetFileName(path));
                File.Move(path, dest, overwrite: true);

                if (!string.IsNullOrWhiteSpace(reason))
                    File.WriteAllText(dest + ".reason.txt", reason);
            }
            catch { }
        }
    }
}
