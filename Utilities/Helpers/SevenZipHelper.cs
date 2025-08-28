using System;
using System.IO;
using System.Linq;
using SevenZip;
using Utilities.Diagnostics;   // EarlyLoginFailures, EarlyFailType
using EarlyFailType = Utilities.Diagnostics.EarlyFailType;

namespace Utilities.Helpers
{
    public static class SevenZipHelper
    {
        private static bool _configured;
        private static readonly object _gate = new();
        private static string? _pathUsed;

        /// <summary>
        /// Configure SevenZipSharp's native library path.
        /// - Tries explicitPath, env var SEVENZIP_LIBRARY_PATH, then common probe locations.
        /// - Returns true on success, false otherwise (and records an early log instead of throwing).
        /// - Idempotent and thread-safe.
        /// </summary>
        public static bool ConfigureLibraryPath(string? explicitPath = null)
        {
            if (_configured) return true;

            lock (_gate)
            {
                if (_configured) return true;

                try
                {
                    // 1) Candidate list (highest to lowest priority)
                    var is64 = Environment.Is64BitProcess;
                    var baseDir = AppContext.BaseDirectory;

                    var envPath = Environment.GetEnvironmentVariable("SEVENZIP_LIBRARY_PATH");

                    var candidates = new[]
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
                    .Distinct()
                    .ToList();

                    var found = candidates.FirstOrDefault(File.Exists);

                    if (found == null)
                    {
                        EarlyLoginFailures.Record(
                            EarlyFailType.KeyfileMissingOrCorrupt,
                            "SevenZip native DLL not found. Probed: " + string.Join(" | ", candidates)
                        );
                        return false;
                    }

                    SevenZipBase.SetLibraryPath(found);
                    _pathUsed = found;
                    _configured = true;
                    return true;
                }
                catch (Exception ex)
                {
                    EarlyLoginFailures.Record(
                        EarlyFailType.KeyfileMissingOrCorrupt,
                        "Failed to configure SevenZip native library.",
                        ex
                    );
                    return false;
                }
            }
        }

        /// <summary>Returns the path that was configured (if any).</summary>
        public static string? GetConfiguredPath() => _pathUsed;
    }
}
