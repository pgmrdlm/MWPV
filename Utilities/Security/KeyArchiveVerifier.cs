using System;
using System.IO;
using System.Linq;
using System.Text;
using SevenZip;

namespace Security.Utility
{
    /// <summary>
    /// Pure verification of the encrypted key archive used at login.
    /// Returns true only if the password unlocks the archive AND required
    /// sentinels exist ("DB_Password.txt" and "keyset.json") and DB_Password.txt is non-empty.
    /// No logging here — callers decide how to log/handle failures.
    /// </summary>
    public static class KeyArchiveVerifier
    {
        private const string SentinelDbPassword = "DB_Password.txt";
        private const string SentinelKeyset = "keyset.json";

        public static bool VerifyPasswordAndSentinels(string archivePath, string password)
        {
            if (string.IsNullOrWhiteSpace(archivePath))
                return false;

            if (!File.Exists(archivePath))
                throw new FileNotFoundException("Key archive not found.", archivePath);

            try
            {
                // SevenZip library path is configured once at app startup.
                using var extractor = new SevenZipExtractor(archivePath, password ?? string.Empty);

                // Require both sentinel entries
                var names = extractor.ArchiveFileNames;
                bool hasDbPassword = names.Contains(SentinelDbPassword, StringComparer.Ordinal);
                bool hasKeyset = names.Contains(SentinelKeyset, StringComparer.Ordinal);
                if (!hasDbPassword || !hasKeyset)
                    return false;

                // Ensure DB_Password.txt is readable & non-empty
                using var ms = new MemoryStream();
                extractor.ExtractFile(SentinelDbPassword, ms);
                ms.Position = 0;
                using var sr = new StreamReader(ms, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
                string secret = sr.ReadToEnd();
                if (string.IsNullOrWhiteSpace(secret))
                    return false;

                return true;
            }
            catch (SevenZipArchiveException)
            {
                // Wrong password / unsupported / unencrypted archive
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                // Can't read file: behave like "verification failed" so caller can retry
                return false;
            }
            catch
            {
                // Unexpected: let caller handle/log as KeyFileVerifyError
                throw;
            }
        }
    }
}
