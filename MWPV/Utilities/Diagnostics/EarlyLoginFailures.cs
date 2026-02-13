// Utilities/Diagnostics/EarlyLoginFailures.cs — full file
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Utilities.Helpers;          // AppPaths

namespace Utilities.Diagnostics
{
    /// <summary>
    /// DPAPI-protected early logs written before DB is available.
    /// Format:
    ///   Line1 (header):  ELOGJSON|1|dpapi
    ///   Body (base64) :  Protect( UTF8(JSON) , entropy=header, CurrentUser )
    /// </summary>
    public static partial class EarlyLoginFailures
    {
        public const string Header = "ELOGJSON|1|dpapi";
        public const string FileExt = ".elogp";

        public static string StoreDir =>
            Path.Combine(AppPaths.LocalAppDataRoot(), "MWPV", "early");


        public static string QuarantineDir =>
            Path.Combine(StoreDir, "quarantine");

        static EarlyLoginFailures()
        {
            TryEnsureDir(StoreDir);
            TryEnsureDir(QuarantineDir);
        }

        /// <summary>Primary API to persist an early failure.</summary>
        public static void Write(string category, string message, string? relatedFile = null, Exception? ex = null)
        {
            try
            {
                var entry = new EarlyEntry(
                    whenUtc: DateTime.UtcNow,
                    category: category,
                    message: message,
                    relatedFile: relatedFile,
                    exType: ex?.GetType().FullName,
                    exMessage: ex?.Message,
                    exStack: ex?.StackTrace
                );

                var json = JsonSerializer.Serialize(entry);
                var bytes = Encoding.UTF8.GetBytes(json);

                // DPAPI with deterministic entropy = header string
                var entropy = Encoding.ASCII.GetBytes(Header);
                var cipher = ProtectedData.Protect(bytes, entropy, DataProtectionScope.CurrentUser);
                var b64 = Convert.ToBase64String(cipher);

                var name = $"{DateTime.UtcNow:yyyyMMdd-HHmmssZ}_{Guid.NewGuid():N}{FileExt}";
                var path = Path.Combine(StoreDir, name);

                using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var sw = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                sw.Write(Header);
                sw.Write("\n");
                sw.Write(b64);
            }
            catch
            {
                // last-ditch path; no rethrow (cannot block app on logging)
            }
        }

        /// <summary>Legacy alias used in a few call sites.</summary>
        public static void Record(string category, string message, string? relatedFile = null, Exception? ex = null)
            => Write(category, message, relatedFile, ex);

        // --- Compatibility overloads ---
        public static void Record(string category, string message, Exception ex)
            => Write(category, message, relatedFile: null, ex);

        public static void Record(Enum stageEnum, string message, string? relatedFile = null, Exception? ex = null)
            => Write(stageEnum.ToString(), message, relatedFile, ex);

        public static void Record(Enum stageEnum, string message, Exception ex)
            => Write(stageEnum.ToString(), message, relatedFile: null, ex);

        /// <summary>Read, validate header, DPAPI-unprotect, and parse entry.</summary>
        internal static bool TryReadAndDecrypt(
            string path,
            out EarlyEntry? entry,
            out string? reason,
            out byte[]? rawJson,
            out byte[]? cipherBytes)
        {
            entry = null; reason = null; rawJson = null; cipherBytes = null;

            try
            {
                var all = File.ReadAllText(path, new UTF8Encoding(false));
                var idx = all.IndexOf('\n');
                if (idx < 0) { reason = "missing newline after header"; return false; }

                var hdr = all[..idx].TrimEnd('\r');
                if (!string.Equals(hdr, Header, StringComparison.Ordinal))
                {
                    reason = $"bad header: {hdr}";
                    return false;
                }

                var b64 = all[(idx + 1)..].Trim();
                if (b64.Length == 0) { reason = "empty body"; return false; }

                cipherBytes = Convert.FromBase64String(b64);

                var entropy = Encoding.ASCII.GetBytes(Header);
                var jsonBlob = ProtectedData.Unprotect(cipherBytes, entropy, DataProtectionScope.CurrentUser);
                rawJson = jsonBlob;

                entry = JsonSerializer.Deserialize<EarlyEntry>(jsonBlob);
                if (entry is null) { reason = "json null"; return false; }

                return true;
            }
            catch (FormatException fe) { reason = "base64: " + fe.Message; return false; }
            catch (CryptographicException ce) { reason = "dpapi: " + ce.Message; return false; }
            catch (Exception ex) { reason = ex.Message; return false; }
        }

        private static void TryEnsureDir(string dir)
        {
            try { Directory.CreateDirectory(dir); } catch { }
        }

        public sealed record EarlyEntry(
            DateTime whenUtc,
            string category,
            string message,
            string? relatedFile,
            string? exType,
            string? exMessage,
            string? exStack);
    }
}
