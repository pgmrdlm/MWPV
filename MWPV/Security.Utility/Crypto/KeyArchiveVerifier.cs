// Security.Utility/Crypto/KeyArchiveVerifier.cs
// Verifies an encrypted 7z "keyset.json" archive using SevenZipSharp.

using System;
using System.IO;
using System.Linq;
using System.Text;
using SevenZip; // Squid-Box.SevenZipSharp (1.6.x)

namespace Security.Utility.Crypto
{
    public static class KeyArchiveVerifier
    {
        private const string KeysetJsonName = "keyset.json";

        /// <summary>
        /// Quick check that <paramref name="archivePath"/> is an encrypted 7z that
        /// contains a non-directory entry named "keyset.json" and that
        /// <paramref name="password"/> successfully decrypts it.
        /// Returns false and a reason string if anything fails.
        /// </summary>
        public static bool VerifyPasswordAndSentinels(
            string archivePath,
            string password,
            out string reason)
        {
            reason = string.Empty;

            if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
            {
                reason = "ArchiveNotFound";
                return false;
            }

            try
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine(
                    $"[KAV] path='{archivePath}' pw='{password}' len={password?.Length ?? 0}");
#endif
                using var extractor = new SevenZipExtractor(archivePath, password);

                // find "keyset.json" (either full path or file name)
                var entry = extractor.ArchiveFileData.FirstOrDefault(f =>
                       string.Equals(f.FileName, KeysetJsonName, StringComparison.Ordinal)
                    || string.Equals(Path.GetFileName(f.FileName), KeysetJsonName, StringComparison.Ordinal));

                if (entry == null)
                {
                    reason = "MissingKeysetJson";
                    return false;
                }
                if (entry.IsDirectory)
                {
                    reason = "KeysetEntryIsDirectory";
                    return false;
                }

                // Try to decrypt and read the entry; will throw on wrong password/corruption.
                using var ms = new MemoryStream();
                extractor.ExtractFile(entry.Index, ms);

                if (ms.Length == 0)
                {
                    reason = "KeysetJsonEmpty";
                    return false;
                }

                // Optional lightweight sanity: confirm it's UTF-8 text (no strict JSON parse here).
                ms.Position = 0;
                using var sr = new StreamReader(ms, Encoding.UTF8, true, 1024, leaveOpen: true);
                _ = sr.ReadLine(); // touching ensures decoding path executes

                reason = "OK";
                return true;
            }
            catch (SevenZipException ex)
            {
                // This is the most common failure path for a wrong password / unsupported archive.
                reason = $"SevenZip:{ex.Message}";
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[KAV] SevenZip error: {ex.Message}");
#endif
                return false;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[KAV] Unexpected: {ex.GetType().Name}: {ex.Message}");
#endif
                return false;
            }
        }

        public static bool VerifyPasswordAndSentinels(string archivePath, string password)
            => VerifyPasswordAndSentinels(archivePath, password, out _);
    }
}
