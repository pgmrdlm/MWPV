// File: MWPV/Utilities/Diagnostics/EarlyLoginFailures.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Utilities.Diagnostics
{
    // Public + same name so all existing call-sites keep working.
    public static partial class EarlyLoginFailures
    {
        // ---------- Header constants & regex (matches your Notepad++ test) ----------
        public const string HeaderPrefix = "ELOGJSON";
        public const int HeaderVersion = 1;
        public const string HeaderVariantPlain = "plain";
        public const string HeaderRegexPattern =
            @"^ELOGJSON\|(?<ver>\d+)(?:\|(?<variant>[A-Za-z0-9_-]+))?$";
        public static readonly Regex HeaderRegex =
            new Regex(HeaderRegexPattern, RegexOptions.Compiled);

        // DEBUG trace that fires when the class is loaded.
        static EarlyLoginFailures()
        {
#if DEBUG
            Debug.WriteLine("[EARLY] EarlyLoginFailures loaded.");
#endif
        }

        // ---------- Paths ----------
        public static string StoreDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MWPV", "early");
        public static string QuarantineDir => Path.Combine(StoreDir, "quarantine");

        [Conditional("DEBUG")]
        private static void Trace(string msg) => Debug.WriteLine($"[EARLY] {msg}");

        // ---------- API preserved for existing code ----------
        public static bool HasPending()
        {
            try
            {
                Directory.CreateDirectory(StoreDir);
                return Directory.EnumerateFiles(StoreDir, "*.elogp").Any();
            }
            catch (Exception ex) { Trace("HasPending error: " + ex); return false; }
        }

        public static string[] EnumeratePendingPaths()
        {
            try
            {
                Directory.CreateDirectory(StoreDir);
                return Directory.EnumerateFiles(StoreDir, "*.elogp").OrderBy(p => p).ToArray();
            }
            catch (Exception ex) { Trace("EnumeratePendingPaths error: " + ex); return Array.Empty<string>(); }
        }

        public static void Quarantine(string fullPath, string reason)
        {
            try
            {
                Directory.CreateDirectory(QuarantineDir);
                var baseName = Path.GetFileNameWithoutExtension(fullPath);
                var dest = Path.Combine(QuarantineDir,
                    $"{DateTime.UtcNow:yyyyMMddTHHmmssfff}_{baseName}_{Sanitize(reason)}.elogp");
                File.Move(fullPath, dest, true);
                Trace($"Quarantined '{fullPath}' :: {reason}");
            }
            catch (Exception ex) { Trace("Quarantine failed: " + ex); }
        }

        private static string Sanitize(string s) =>
            new string(s.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());

        // ---------- Record overloads to satisfy all call-sites ----------
        public static void Record(string category, string message, Exception? ex = null, string? relatedFile = null)
        {
            try
            {
                Directory.CreateDirectory(StoreDir);
                var path = Path.Combine(StoreDir, $"{DateTime.UtcNow:yyyyMMddTHHmmssfff}_{Guid.NewGuid():N}.elogp");

                // Always write a PLAINTEXT header line so TryRead can regex it.
                var header = $"{HeaderPrefix}|{HeaderVersion}|{HeaderVariantPlain}";
#if DEBUG
                Trace($"Record start path='{path}' header='{header}' cat='{category}' msg='{message}'");
#endif
                var body = new EarlyEntry(
                    whenUtc: DateTime.UtcNow,
                    category: category,
                    message: message,
                    relatedFile: string.IsNullOrWhiteSpace(relatedFile) ? null : relatedFile,
                    exType: ex?.GetType().FullName,
                    exMessage: ex?.Message,
                    exStack: ex?.ToString()
                );

                // Header line (plaintext) + newline + JSON body (UTF-8 without BOM).
                using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var sw = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                sw.WriteLine(header);
                sw.Write(JsonSerializer.Serialize(body));
                sw.Flush();
#if DEBUG
                Trace($"Record wrote '{path}' bytes={fs.Length}");
#endif
            }
            catch (Exception wex) { Trace("Record failed: " + wex); }
        }

        // Legacy convenience overloads some callers likely use:
        public static void Record(string message) => Record("INFO", message, null, null);
        public static void Record(Exception ex) => Record("EXCEPTION", ex.Message, ex, null);
        public static void Record(string category, Exception ex) => Record(category, ex.Message, ex, null);
        public static void Record(string category, string message, string relatedFile) => Record(category, message, null, relatedFile);

        // ---------- Reader used by the ingestor ----------
        public static bool TryRead(string fullPath, out EarlyEntry? entry, out string failure)
        {
            entry = null; failure = "";
            try
            {
                using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

                var rawFirst = sr.ReadLine();
                var firstLine = (rawFirst ?? string.Empty).Trim();
                if (firstLine.Length > 0 && firstLine[0] == '\uFEFF') firstLine = firstLine.Substring(1);
#if DEBUG
                Trace($"TryRead '{fullPath}' header='{firstLine}'");
#endif
                var m = HeaderRegex.Match(firstLine);
#if DEBUG
                Trace($"TryRead header match success={m.Success}");
#endif
                if (!m.Success) { failure = "parse_failed:bad_header"; return false; }

                var variant = m.Groups["variant"].Success ? m.Groups["variant"].Value : HeaderVariantPlain;
                var rest = sr.ReadToEnd();

                if (variant.Equals(HeaderVariantPlain, StringComparison.OrdinalIgnoreCase))
                {
                    entry = JsonSerializer.Deserialize<EarlyEntry>(rest);
                    if (entry == null) { failure = "parse_failed:json_null"; return false; }
                    return true;
                }

                failure = $"parse_failed:unsupported_variant_{variant}";
                return false;
            }
            catch (Exception ex) { failure = $"read_failed:{ex.GetType().Name}"; Trace("TryRead failed: " + ex); return false; }
        }

        // Body shape
        public sealed record EarlyEntry(DateTime whenUtc, string category, string message,
                                        string? relatedFile, string? exType, string? exMessage, string? exStack);
    }
}
