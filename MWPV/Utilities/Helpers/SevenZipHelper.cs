// Utilities/Helpers/SevenZipHelper.cs — full rewrite
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using SevenZip;
using Utilities.Diagnostics;     // EarlyLoginFailures
using Security.Utility;          // EarlyFailType
using Utilities.Helpers.Debugging;
using EarlyFail = Security.Utility.EarlyFailType;

namespace Utilities.Helpers
{
    public static class SevenZipHelper
    {
        private static bool _configured;
        private static readonly object _gate = new();
        private static string? _pathUsed;

        /// <summary>
        /// Configure SevenZipSharp's native library path.
        /// Tries: explicitPath → SEVENZIP_LIBRARY_PATH → app baseDir locations.
        /// Returns true on success; false on failure (and logs an early failure).
        /// Thread-safe & idempotent.
        /// </summary>
        public static bool ConfigureLibraryPath(string? explicitPath = null)
        {
            if (_configured) return true;

            lock (_gate)
            {
                if (_configured) return true;

                try
                {
                    var is64 = Environment.Is64BitProcess;
                    var baseDir = AppContext.BaseDirectory;
#if DEBUG
                    Dbg($"ConfigureLibraryPath called. explicitPath={explicitPath ?? "<null>"} baseDir={baseDir} is64={is64}");
#endif
                    var envPath = Environment.GetEnvironmentVariable("SEVENZIP_LIBRARY_PATH");

                    // Build candidate list (highest → lowest priority)
                    var candidates = new List<string?>()
                    {
                        explicitPath,
                        string.IsNullOrWhiteSpace(envPath) ? null : envPath,

                        // App root (NuGet or manual copy)
                        Path.Combine(baseDir, "7z.dll"),
                        Path.Combine(baseDir, is64 ? "7z64.dll" : "7z.dll"),

                        // Typical NuGet runtimes layout
                        Path.Combine(baseDir, "runtimes", is64 ? "win-x64" : "win-x86", "native", "7z.dll"),

                        // Alternative native folders you might use
                        Path.Combine(baseDir, "native", is64 ? "x64" : "x86", "7z.dll"),
                    }
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(p => Path.GetFullPath(p!))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

#if DEBUG
                    Dbg("Probe candidates:\n" + string.Join("\n", candidates));
#endif
                    var found = candidates.FirstOrDefault(File.Exists);

                    if (found == null)
                    {
                        var msg = "SevenZip native DLL not found.";
                        EarlyLoginFailures.Record(EarlyFail.InvalidKeyFile, msg + " (See Debug output for probe list)");
#if DEBUG
                        Dbg(msg + "\n(no candidates existed on disk)");
#endif
                        return false;
                    }

                    // Set and remember
                    SevenZipBase.SetLibraryPath(found);
                    _pathUsed = found;
                    _configured = true;
#if DEBUG
                    Dbg("SevenZip library path set to: " + _pathUsed);
#endif
                    return true;
                }
                catch (SevenZipLibraryException ex)
                {
                    // Library present but load failed (often bitness mismatch)
                    var msg = "Failed to load SevenZip native library (bitness mismatch or invalid DLL).";
                    EarlyLoginFailures.Record(EarlyFail.InvalidKeyFile, msg, ex);
#if DEBUG
                    Dbg(msg + " Exception=" + ex.Message);
#endif
                    return false;
                }
                catch (Exception ex)
                {
                    var msg = "Unexpected error while configuring SevenZip native library.";
                    EarlyLoginFailures.Record(EarlyFail.InvalidKeyFile, msg, ex);
#if DEBUG
                    Dbg(msg + " Exception=" + ex);
#endif
                    return false;
                }
            }
        }

        /// <summary>Returns the path that was configured (if any).</summary>
        public static string? GetConfiguredPath() => _pathUsed;

#if DEBUG
        [Conditional("DEBUG")]
        private static void Dbg(string msg)
            => Debug.WriteLine($"[SevenZipHelper] {msg}");
#endif
    }
}
