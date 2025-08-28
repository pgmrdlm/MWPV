// Utilities/Security/SensitiveDataCleaner.cs
using System;
using System.Collections.Concurrent;
using System.Diagnostics;           // Debug.WriteLine traces (always-on)
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Security.Utility.Wiping;

/// <summary>
/// Secure file/dir deletion + sensitive-memory cleanup registry.
/// - File side: secure delete helpers and folder sweep/purge.
/// - Memory side: register buffers/stores; App calls WipeAll() on normal/abnormal shutdown.
///
/// Notes:
/// - Traces use Debug.WriteLine and are NOT wrapped in #if DEBUG (you'll see them if a listener is attached).
/// - No secrets are ever printed.
/// </summary>
public static class SensitiveDataCleaner
{
    // ===== Central paths (align with EarlyLoginFailures) =====
    public static readonly string EarlyDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MWPV", "early");
    public static readonly string QuarantineDir = Path.Combine(EarlyDir, "quarantine");

    // ===== Memory wipe registry =====
    private static readonly ConcurrentBag<Action> _actions = new();
    private static int _wiped = 0;

    /// <summary>Register a custom wipe action (e.g., zero a buffer, clear a store).</summary>
    public static void Register(Action wipeAction)
    {
        if (wipeAction == null) return;
        _actions.Add(wipeAction);
        Debug.WriteLine("[CLEANER] Registered wipe action (custom).");
    }

    /// <summary>Register any object that knows how to wipe itself.</summary>
    public static void Register(ISensitiveWipe wipable)
    {
        if (wipable == null) return;
        _actions.Add(() =>
        {
            try { wipable.Wipe(); } catch { /* swallow */ }
        });
        Debug.WriteLine("[CLEANER] Registered wipe action (ISensitiveWipe).");
    }

    /// <summary>
    /// Idempotent global wipe entrypoint. Zeroizes all registered buffers/stores
    /// and runs a best-effort GC to reduce residuals.
    /// </summary>
    public static void WipeAll()
    {
        if (Interlocked.Exchange(ref _wiped, 1) == 1)
        {
            Debug.WriteLine("[WIPE] SensitiveDataCleaner.WipeAll skipped (already executed).");
            return;
        }

        int ran = 0;
        while (_actions.TryTake(out var a))
        {
            try { a(); }
            catch { /* swallow */ }
            ran++;
        }

        Debug.WriteLine($"[WIPE] SensitiveDataCleaner.WipeAll executed. registry_actions_ran={ran}");

        // Best-effort: force collection/compaction to trim residuals
        try
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        }
        catch { /* swallow */ }
    }

    // -----------------------
    // Convenience zeroizers
    // -----------------------
    public static void Zero(byte[]? buffer)
    {
        try
        {
            if (buffer == null) return;
            Array.Clear(buffer, 0, buffer.Length);
        }
        catch { }
    }

    public static void Zero(char[]? buffer)
    {
        try
        {
            if (buffer == null) return;
            Array.Clear(buffer, 0, buffer.Length);
        }
        catch { }
    }

    public static void Zero(SecureString? ss)
    {
        try { ss?.Clear(); } catch { }
    }

    // ===== Secure file deletion =====

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
            bool deleted = !File.Exists(fi.FullName);
            Debug.WriteLine($"[SECDEL] file='{Path.GetFileName(path)}' deleted={deleted}");
            return deleted;
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
    public static (int total, int deleted) SecureDeleteDirectory(
        string dir,
        string searchPattern = "*",
        SearchOption option = SearchOption.TopDirectoryOnly,
        int overwritePasses = 1,
        bool shredNames = true,
        bool finalZeroPass = true,
        bool removeDirectories = false)
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
        catch
        {
            return (total, deleted);
        }
        finally
        {
            if (removeDirectories)
            {
                try
                {
                    foreach (var d in Directory.EnumerateDirectories(dir, "*", option))
                    {
                        try { Directory.Delete(d, recursive: true); } catch { }
                    }
                }
                catch { }
            }
            Debug.WriteLine($"[SECDEL] dir='{dir}' pattern='{searchPattern}' total={total} deleted={deleted}");
        }
    }

    /// <summary>
    /// Back-compat shim used around the app. Deletes all files in a folder (top-level) using secure delete.
    /// </summary>
    public static (int total, int deleted) SecureDeleteAllFiles(
        string dir,
        int overwritePasses = 1,
        bool shredNames = true,
        bool finalZeroPass = true,
        bool removeDirectories = false)
        => SecureDeleteDirectory(dir, "*", SearchOption.TopDirectoryOnly, overwritePasses, shredNames, finalZeroPass, removeDirectories);

    // Back-compat overload used by older call sites that omit pattern/option but use named args
    public static (int total, int deleted) SecureDeleteDirectory(
        string dir,
        int overwritePasses = 1,
        bool shredNames = true,
        bool finalZeroPass = true)
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
            Debug.WriteLine($"[SECDEL] sweep early residuals total={total} deleted={deleted}");
            return (total, deleted);
        }
        catch
        {
            return (0, 0);
        }
    }

    /// <summary>Purge quarantine items older than the provided number of days. Does NOT delete fresh quarantines.</summary>
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
        Debug.WriteLine($"[SECDEL] purge quarantine olderThanDays={olderThanDays} total={total} removed={removed}");
        return (total, removed);
    }

    // ===== Back-compat memory wipe shims (kept) =====
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
    /*public static void Clear(TextBox? tb)
    { try { if (tb != null) tb.Text = string.Empty; } catch { } }

    public static void Clear(ref TextBox? tb)
    { try { if (tb != null) tb.Text = string.Empty; } catch { } }

    public static void Clear(PasswordBox? pb)
    { try { pb?.Clear(); } catch { } }
 
    public static void Clear(ref PasswordBox? pb)
    { try { pb?.Clear(); } catch { } }
       */

    public static void Clear(SecureString? ss)
    { try { ss?.Clear(); } catch { } }

    public static void Clear(ref SecureString? ss)
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

    // ===== Optional runtime diagnostics (always available) =====
    public static void DebugStatus(string? note = null)
    {
        Debug.WriteLine($"[DEBUG] SensitiveDataCleaner: wiped={_wiped != 0}, pending_actions={_actions.Count}" +
            (string.IsNullOrEmpty(note) ? "" : $" note={note}"));
    }

    public static void DebugPing(string? note = null)
    {
        Debug.WriteLine("[DEBUG] SensitiveDataCleaner ping " + (note ?? ""));
    }
}

/// <summary>Implement on classes that hold secrets in memory so the cleaner can wipe them.</summary>
public interface ISensitiveWipe
{
    void Wipe();
}
