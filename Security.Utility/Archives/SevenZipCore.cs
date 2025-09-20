using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using SevenZip;

namespace Security.Utility.Archives
{
    /// <summary>
    /// Centralized SevenZip setup + tiny factories.
    /// Thread-safe, idempotent, no app-level logging.
    /// </summary>
    public static class SevenZipCore
    {
        private static readonly object _gate = new();
        private static bool _configured;
        private static string? _pathUsed;

        /// <summary>
        /// Ensures SevenZipSharp has a valid native library bound.
        /// Probes: explicitPath → SEVENZIP_LIBRARY_PATH → app baseDir locations.
        /// </summary>
        /// <param name="explicitPath">Optional explicit path to 7z.dll/7z64.dll</param>
        /// <returns>(ok, reason). ok=false with reason on failure (no exceptions).</returns>
        public static (bool ok, string? reason) EnsureConfigured(string? explicitPath = null)
        {
            if (_configured) return (true, null);

            lock (_gate)
            {
                if (_configured) return (true, null);

                try
                {
                    var is64 = Environment.Is64BitProcess;
                    var baseDir = AppContext.BaseDirectory;
                    var envPath = Environment.GetEnvironmentVariable("SEVENZIP_LIBRARY_PATH");

#if DEBUG
                    Dbg($"EnsureConfigured: explicitPath={explicitPath ?? "<null>"} baseDir={baseDir} is64={is64}");
#endif

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
                        return (false, "SevenZip native DLL not found");

                    SevenZipBase.SetLibraryPath(found);
                    _pathUsed = found;
                    _configured = true;

#if DEBUG
                    Dbg("SevenZip library path set to: " + _pathUsed);
#endif
                    return (true, null);
                }
                catch (SevenZipLibraryException ex)
                {
#if DEBUG
                    Dbg("Failed to load SevenZip native library: " + ex.Message);
#endif
                    return (false, "Failed to load SevenZip native library (bitness mismatch or invalid DLL)");
                }
                catch (Exception ex)
                {
#if DEBUG
                    Dbg("Unexpected error while configuring SevenZip: " + ex);
#endif
                    return (false, "Unexpected error while configuring SevenZip");
                }
            }
        }

        /// <summary>Returns the path bound to SevenZipSharp (if any).</summary>
        public static string? GetConfiguredPath() => _pathUsed;

        /// <summary>
        /// Ensure configured, then create a SevenZipExtractor with the given archive and password.
        /// Throws if configuration fails.
        /// </summary>
        public static SevenZipExtractor CreateExtractor(string archivePath, string? password)
        {
            var (ok, reason) = EnsureConfigured();
            if (!ok) throw new InvalidOperationException(reason ?? "SevenZip not configured");

            return password is null
                ? new SevenZipExtractor(archivePath)
                : new SevenZipExtractor(archivePath, password);
        }

        /// <summary>
        /// Ensure configured, then create a SevenZipCompressor with sensible defaults.
        /// Caller may override properties before use.
        /// </summary>
        public static SevenZipCompressor CreateCompressor()
        {
            var (ok, reason) = EnsureConfigured();
            if (!ok) throw new InvalidOperationException(reason ?? "SevenZip not configured");

            return new SevenZipCompressor
            {
                ArchiveFormat = OutArchiveFormat.SevenZip,
                CompressionLevel = CompressionLevel.Normal,
                CompressionMethod = CompressionMethod.Lzma2,
                EncryptHeaders = true,
                ZipEncryptionMethod = ZipEncryptionMethod.Aes256,
                PreserveDirectoryRoot = false
            };
        }

#if DEBUG
        [Conditional("DEBUG")]
        private static void Dbg(string msg) =>
            Debug.WriteLine($"[SevenZipCore] {msg}");
#endif
    }
}
