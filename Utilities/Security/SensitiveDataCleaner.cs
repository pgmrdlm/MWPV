using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Utilities.Helpers;

namespace Utilities.Security
{
    /// <summary>
    /// Provides utilities to securely wipe sensitive data from memory and disk.
    /// 
    /// <para><b>Memory:</b> helpers to wipe <see cref="SecureString"/>, <c>string</c>, <c>char[]</c>, <c>byte[]</c>,
    /// and common collections without leaving readable remnants.</para>
    /// <para><b>Disk:</b> secure file deletion that overwrites file contents, scrubs timestamps, optionally shreds names,
    /// and removes files with resilient retry logic and a delete-on-reboot fallback. Directory helpers enumerate and
    /// securely delete entire trees.</para>
    /// 
    /// <remarks>
    /// Design goals:
    /// <list type="bullet">
    /// <item><description>Minimize copies of sensitive material in managed memory.</description></item>
    /// <item><description>Be resilient to transient sharing violations (e.g., AV scanners/compressors).</description></item>
    /// <item><description>Preserve existing public signatures to avoid breaking callers.</description></item>
    /// </list>
    /// </remarks>
    /// </summary>
    public static class SensitiveDataCleaner
    {
        // =========================================================
        // UI helpers (WPF)
        // =========================================================

        /// <summary>Clears the provided <see cref="System.Windows.Controls.TextBox"/> (best-effort).</summary>
        public static void Clear(System.Windows.Controls.TextBox tb) => tb?.Clear();

        /// <summary>Clears the provided <see cref="System.Windows.Controls.PasswordBox"/> (best-effort).</summary>
        public static void Clear(System.Windows.Controls.PasswordBox pb) => pb?.Clear();

        // =========================================================
        // SecureString
        // =========================================================

        /// <summary>
        /// Overwrites and clears a <see cref="SecureString"/> in place.
        /// </summary>
        /// <param name="secure">The secure string to wipe.</param>
        public static void WipeSecureString(SecureString secure)
        {
            if (secure == null || secure.Length == 0) return;

            IntPtr unmanaged = IntPtr.Zero;
            try
            {
                unmanaged = Marshal.SecureStringToGlobalAllocUnicode(secure);
                for (int i = 0; i < secure.Length; i++)
                    Marshal.WriteInt16(unmanaged, i * 2, '\0');
            }
            finally
            {
                Marshal.ZeroFreeGlobalAllocUnicode(unmanaged);
                secure.Clear();
            }
        }

        // =========================================================
        // char[] wipes
        // =========================================================

        /// <summary>
        /// Overwrites a char array with cryptographically random data, then zeros it.
        /// </summary>
        public static void WipeCharArray(char[] data)
        {
            if (data == null || data.Length == 0) return;

            try
            {
                var overwritePattern = new char[data.Length];
                var randomBytes = new byte[data.Length * sizeof(char)];
                RandomNumberGenerator.Fill(randomBytes);
                Buffer.BlockCopy(randomBytes, 0, overwritePattern, 0, randomBytes.Length);

                for (int i = 0; i < data.Length; i++)
                    data[i] = overwritePattern[i];

                Array.Clear(overwritePattern, 0, overwritePattern.Length);
                Array.Clear(data, 0, data.Length);
            }
            catch
            {
                throw;
            }
        }

        /// <summary>
        /// Wipes a char array and nulls the reference.
        /// </summary>
        public static void WipeCharArray(ref char[] data)
        {
            if (data == null || data.Length == 0) { data = null; return; }
            WipeCharArray(data);
            data = null;
        }

        // =========================================================
        // byte[] wipes
        // =========================================================

        /// <summary>
        /// Overwrites a byte array with cryptographically random data, then zeros it.
        /// </summary>
        public static void WipeByteArray(byte[] data)
        {
            if (data == null || data.Length == 0) return;

            try
            {
                RandomNumberGenerator.Fill(data);
                Array.Clear(data, 0, data.Length);
            }
            catch
            {
                throw;
            }
        }

        /// <summary>
        /// Wipes a byte array and nulls the reference.
        /// </summary>
        public static void WipeByteArray(ref byte[] data)
        {
            if (data == null || data.Length == 0) { data = null; return; }
            WipeByteArray(data);
            data = null;
        }

        // =========================================================
        // string / StringBuilder wipes
        // =========================================================

        /// <summary>
        /// Best-effort wipe of a string by copying to a char[] and wiping that, then nulling the reference.
        /// Note: strings are immutable; the original instance may persist elsewhere in memory.
        /// </summary>
        public static void WipeString(ref string data)
        {
            try
            {
                if (string.IsNullOrEmpty(data)) { data = null; return; }
                char[] chars = data.ToCharArray();
                WipeCharArray(chars);
                data = null;
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Failed to securely wipe string", stage: "wipe-string-error");
            }
        }

        /// <summary>
        /// Best-effort wipe for <see cref="StringBuilder"/> contents with minimal copying.
        /// </summary>
        public static void WipeStringBuilder(StringBuilder sb)
        {
            if (sb == null || sb.Length == 0) return;
            try
            {
#if NET5_0_OR_GREATER
                foreach (var chunk in sb.GetChunks())
                {
                    var tmp = chunk.Span.ToArray();
                    WipeCharArray(tmp);
                }
                sb.Clear();
#else
                for (int i = 0; i < sb.Length; i++) sb[i] = '\0';
                sb.Clear();
#endif
            }
            catch { /* best effort */ }
        }

        /// <summary>
        /// Convenience wipe: randomizes and nulls <paramref name="password"/>; nulls <paramref name="archivePath"/>.
        /// </summary>
        public static void WipeSensitiveStrings(ref string password, ref string archivePath)
        {
            if (!string.IsNullOrEmpty(password))
            {
                try { password = SecurePassword.GenerateAsString(password.Length); }
                catch { password = RandomizedAscii(password.Length); }
                password = null;
            }
            archivePath = null;
        }

        private static string RandomizedAscii(int len)
        {
            if (len <= 0) return string.Empty;
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*()-_=+[]{};:,.<>?";
            var bytes = new byte[len];
            RandomNumberGenerator.Fill(bytes);
            var chars = new char[len];
            for (int i = 0; i < len; i++) chars[i] = alphabet[bytes[i] % alphabet.Length];
            return new string(chars);
        }

        // =========================================================
        // Collection helpers
        // =========================================================

        /// <summary>Wipes all arrays in the list and clears the list.</summary>
        public static void WipeAndClear(IList<char[]> arrays)
        {
            if (arrays == null) return;
            for (int i = 0; i < arrays.Count; i++)
            {
                WipeCharArray(arrays[i]);
                arrays[i] = null;
            }
            arrays.Clear();
        }

        /// <summary>Wipes all values and clears the dictionary.</summary>
        public static void WipeAndClear(IDictionary<string, char[]> dict)
        {
            if (dict == null) return;
            foreach (var key in new List<string>(dict.Keys))
            {
                WipeCharArray(dict[key]);
                dict[key] = null;
                dict.Remove(key);
            }
        }

        /// <summary>Disposes a disposable object and nulls the reference.</summary>
        public static void DisposeAndNull<T>(ref T disposable) where T : class, IDisposable
        {
            if (disposable == null) return;
            try { disposable.Dispose(); } catch { /* swallow */ } finally { disposable = null; }
        }

        // =========================================================
        // DISK: secure file / folder deletion
        // =========================================================

        // Win32 delete-on-reboot fallback
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool MoveFileEx(string lpExistingFileName, string lpNewFileName, int dwFlags);
        private const int MOVEFILE_DELAY_UNTIL_REBOOT = 0x4;

        /// <summary>
        /// Securely deletes every file in a directory (optionally recursive).
        /// </summary>
        /// <param name="folderPath">Directory whose files should be securely deleted.</param>
        /// <param name="overwritePasses">Number of overwrite passes per file (min 1).</param>
        /// <param name="recursive">If true, processes all subdirectories.</param>
        /// <param name="shredNames">If true, randomly renames files before deletion.</param>
        /// <param name="finalZeroPass">If true, performs a final zero pass after random overwrites.</param>
        public static void SecureDeleteAllFiles(
            string folderPath,
            int overwritePasses = 1,
            bool recursive = false,
            bool shredNames = true,
            bool finalZeroPass = false)
        {
            string stage = "init";
            try
            {
                stage = "check-exists";
                if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                {
                    Debug.WriteLine($"Folder does not exist: {folderPath}");
                    return;
                }

                stage = "enumerate";
                var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

                foreach (var file in Directory.EnumerateFiles(folderPath, "*", option))
                {
                    bool success = SecureFileDelete(file, overwritePasses, shredNames, finalZeroPass);
                    if (!success)
                    {
                        Debug.WriteLine($"Failed to securely delete: {file}");
                        // Optionally escalate via ErrorHandler.Abend(...)
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "SecureDeleteAllFiles failed", stage: stage);
            }
        }

        /// <summary>
        /// Securely wipes and deletes a single file: overwrite, optional zero pass, timestamp scrub,
        /// optional randomized rename, and resilient deletion with retries.
        /// </summary>
        /// <param name="filePath">Path to the file to securely delete.</param>
        /// <param name="overwritePasses">Number of random overwrite passes (min 1).</param>
        /// <param name="shredName">If true, randomly rename the file one or more times before deletion.</param>
        /// <param name="finalZeroPass">If true, writes a final zero pass after random passes.</param>
        /// <returns><c>true</c> if the file is removed (or scheduled for removal on reboot), otherwise <c>false</c>.</returns>
        public static bool SecureFileDelete(
            string filePath,
            int overwritePasses = 1,
            bool shredName = true,
            bool finalZeroPass = false)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return true;
            if (!File.Exists(filePath)) return true;
            if (overwritePasses < 1) overwritePasses = 1;

            string stage = "init";
            byte[] buffer = null;
            string currentPath = filePath; // track the live file name in case of rename

            try
            {
                stage = "normalize-attrs";
                TryNormalizeAttributes(currentPath);

                stage = "stat";
                var info = new FileInfo(currentPath);
                long length = info.Length;

                // Fast-path for empty files: scrub metadata, optionally shred name, then delete with retries.
                if (length == 0)
                {
                    stage = "scrub-meta-empty";
                    ScrubTimestampsSafe(currentPath);
                    if (shredName)
                        currentPath = RandomRenameAndGetFinalPath(currentPath);

                    return RobustDelete(currentPath);
                }

                stage = "alloc-buffer";
                const int BufSize = 64 * 1024;
                buffer = new byte[BufSize];

                stage = "open-write";
                using (var stream = new FileStream(
                    currentPath,
                    FileMode.Open,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: BufSize,
                    options: FileOptions.WriteThrough | FileOptions.SequentialScan))
                {
                    for (int pass = 0; pass < overwritePasses; pass++)
                    {
                        stage = $"pass-{pass}-seek";
                        stream.Seek(0, SeekOrigin.Begin);

                        long remaining = length;
                        stage = $"pass-{pass}-loop";
                        while (remaining > 0)
                        {
                            RandomNumberGenerator.Fill(buffer);
                            int toWrite = (int)Math.Min(buffer.Length, remaining);
                            stream.Write(buffer, 0, toWrite);
                            remaining -= toWrite;
                        }
#if NET6_0_OR_GREATER
                        stream.Flush(true);
#else
                        stream.Flush();
#endif
                    }

                    if (finalZeroPass)
                    {
                        Array.Clear(buffer, 0, buffer.Length);
                        stage = "zero-pass-seek";
                        stream.Seek(0, SeekOrigin.Begin);

                        long remaining = length;
                        stage = "zero-pass-loop";
                        while (remaining > 0)
                        {
                            int toWrite = (int)Math.Min(buffer.Length, remaining);
                            stream.Write(buffer, 0, toWrite);
                            remaining -= toWrite;
                        }
#if NET6_0_OR_GREATER
                        stream.Flush(true);
#else
                        stream.Flush();
#endif
                    }
                }

                stage = "truncate";
                using (var trunc = new FileStream(currentPath, FileMode.Open, FileAccess.Write, FileShare.None))
                {
                    trunc.SetLength(0);
#if NET6_0_OR_GREATER
                    trunc.Flush(true);
#else
                    trunc.Flush();
#endif
                }

                stage = "scrub-meta";
                ScrubTimestampsSafe(currentPath);

                stage = "shred-name";
                if (shredName)
                    currentPath = RandomRenameAndGetFinalPath(currentPath);

                stage = "delete";
                if (!RobustDelete(currentPath))
                    throw new IOException("File still exists after secure delete attempts.");

                return true;
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Secure delete failed", stage: stage);
                return false;
            }
            finally
            {
                if (buffer != null) Array.Clear(buffer, 0, buffer.Length);
            }
        }

        /// <summary>
        /// Recursively wipes all files in a directory then deletes (optionally) the directories if empty.
        /// </summary>
        /// <param name="folderPath">Root directory to securely delete.</param>
        /// <param name="overwritePasses">Number of overwrite passes for contained files.</param>
        /// <param name="shredNames">If true, randomly rename files before deletion.</param>
        /// <param name="finalZeroPass">If true, performs a final zero pass on files.</param>
        /// <param name="removeDirectories">If true, attempts to delete directories after files are removed.</param>
        public static void SecureDeleteDirectory(
            string folderPath,
            int overwritePasses = 1,
            bool shredNames = true,
            bool finalZeroPass = false,
            bool removeDirectories = true)
        {
            SecureDeleteAllFiles(folderPath, overwritePasses, recursive: true, shredNames: shredNames, finalZeroPass: finalZeroPass);

            if (!removeDirectories) return;
            try
            {
                foreach (var dir in Directory.GetDirectories(folderPath, "*", SearchOption.AllDirectories))
                {
                    TryNormalizeDirectoryAttributes(dir);
                    TryDeleteDirectory(dir);
                }
                TryDeleteDirectory(folderPath);
            }
            catch (Exception ex)
            {
                // Intentionally Warning: directories left behind aren't sensitive once files are wiped.
                ErrorHandler.Abend(ex, "SecureDeleteDirectory failed", stage: "remove-dirs", severity: ErrorSeverity.Warning);
            }
        }

        // --------------------------
        // Diagnostics & advanced flows
        // --------------------------

        /// <summary>Error categories for <see cref="SecureFileDeleteDiagnose"/> and quarantine flows.</summary>
        public enum SecureDeleteError { None, NotFound, InUse, IoError, Unauthorized, Unknown }

        /// <summary>Result record for secure deletion diagnostics.</summary>
        public sealed class SecureDeleteResult
        {
            /// <summary>True if delete succeeded.</summary>
            public bool Success { get; init; }
            /// <summary>Best-effort error classification.</summary>
            public SecureDeleteError Error { get; init; }
            /// <summary>Optional detail message.</summary>
            public string Detail { get; init; }
        }

        /// <summary>
        /// Instrumented secure delete with categorized errors; useful for quarantined files or troubleshooting.
        /// </summary>
        public static SecureDeleteResult SecureFileDeleteDiagnose(string path, int overwritePasses = 1, int maxRetries = 5)
        {
            try { File.SetAttributes(path, FileAttributes.Normal); } catch { /* best-effort */ }

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.WriteThrough))
                    {
                        long len = fs.Length;
                        byte[] buf = new byte[8192]; // zero fill is fine
                        for (int pass = 0; pass < overwritePasses; pass++)
                        {
                            fs.Position = 0;
                            long remaining = len;
                            while (remaining > 0)
                            {
                                int chunk = (int)Math.Min(buf.Length, remaining);
                                fs.Write(buf, 0, chunk);
                                remaining -= chunk;
                            }
                            fs.Flush(true);
                        }
                    }
                    File.Delete(path);
                    return new SecureDeleteResult { Success = true, Error = SecureDeleteError.None };
                }
                catch (FileNotFoundException ex)
                {
                    return new SecureDeleteResult { Success = false, Error = SecureDeleteError.NotFound, Detail = ex.Message };
                }
                catch (UnauthorizedAccessException ex)
                {
                    Thread.Sleep(60 * (attempt + 1));
                    if (attempt == maxRetries)
                        return new SecureDeleteResult { Success = false, Error = SecureDeleteError.Unauthorized, Detail = ex.Message };
                }
                catch (IOException ex)
                {
                    Thread.Sleep(60 * (attempt + 1));
                    if (attempt == maxRetries)
                        return new SecureDeleteResult { Success = false, Error = SecureDeleteError.InUse, Detail = ex.Message };
                }
                catch (Exception ex)
                {
                    Thread.Sleep(60 * (attempt + 1));
                    if (attempt == maxRetries)
                        return new SecureDeleteResult { Success = false, Error = SecureDeleteError.Unknown, Detail = ex.Message };
                }
            }

            return new SecureDeleteResult { Success = false, Error = SecureDeleteError.Unknown, Detail = "max retries exceeded" };
        }

        /// <summary>
        /// Moves a file into a quarantine folder (same volume), then performs a diagnostic secure delete.
        /// </summary>
        /// <param name="srcPath">File to quarantine and delete.</param>
        /// <param name="quarantineDir">Destination directory; created if missing.</param>
        /// <param name="overwritePasses">Overwrite passes to use.</param>
        /// <param name="maxRetries">Maximum diagnostic retries.</param>
        public static SecureDeleteResult QuarantineThenSecureDelete(
            string srcPath,
            string quarantineDir,
            int overwritePasses = 1,
            int maxRetries = 5)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(srcPath) || !File.Exists(srcPath))
                    return new SecureDeleteResult { Success = true, Error = SecureDeleteError.NotFound };

                Directory.CreateDirectory(quarantineDir);

                // same-volume, atomic move to break races with other code
                var qPath = Path.Combine(quarantineDir, Path.GetFileName(srcPath));
                File.Move(srcPath, qPath);

                return SecureFileDeleteDiagnose(qPath, overwritePasses, maxRetries);
            }
            catch (UnauthorizedAccessException ex)
            {
                return new SecureDeleteResult { Success = false, Error = SecureDeleteError.Unauthorized, Detail = ex.Message };
            }
            catch (IOException ex)
            {
                return new SecureDeleteResult { Success = false, Error = SecureDeleteError.InUse, Detail = ex.Message };
            }
            catch (Exception ex)
            {
                return new SecureDeleteResult { Success = false, Error = SecureDeleteError.Unknown, Detail = ex.Message };
            }
        }

        // --------------------------
        // Internal helpers (file system)
        // --------------------------

        /// <summary>Sets file attributes to <see cref="FileAttributes.Normal"/> (best-effort).</summary>
        private static void TryNormalizeAttributes(string filePath)
        {
            try { File.SetAttributes(filePath, FileAttributes.Normal); }
            catch { /* non-fatal */ }
        }

        /// <summary>Sets directory attributes to <see cref="FileAttributes.Directory"/> | <see cref="FileAttributes.Normal"/> (best-effort).</summary>
        private static void TryNormalizeDirectoryAttributes(string dir)
        {
            try { File.SetAttributes(dir, FileAttributes.Directory | FileAttributes.Normal); }
            catch { /* non-fatal */ }
        }

        /// <summary>Randomizes timestamps to reduce metadata correlation (best-effort).</summary>
        private static void ScrubTimestampsSafe(string filePath)
        {
            try
            {
                var info = new FileInfo(filePath);
                DateTime randomTime = DateTime.UtcNow
                    .AddDays(-Random.Shared.Next(0, 3650))
                    .AddMinutes(-Random.Shared.Next(0, 60 * 24));

                info.CreationTimeUtc = randomTime;
                info.LastAccessTimeUtc = randomTime;
                info.LastWriteTimeUtc = randomTime;
            }
            catch { /* non-fatal */ }
        }

        /// <summary>
        /// Performs one or more randomized renames and returns the final path.
        /// </summary>
        private static string RandomRenameAndGetFinalPath(string filePath)
        {
            try
            {
                int renameCount = Random.Shared.Next(1, 4);
                string dir = Path.GetDirectoryName(filePath)!;
                string current = filePath;
                string ext = Path.GetExtension(current);

                for (int i = 0; i < renameCount; i++)
                {
                    string newName = GetRandomFileNameLike(current);
                    string candidate = Path.Combine(dir, newName + ext);

                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        if (!File.Exists(candidate))
                        {
                            File.Move(current, candidate);
                            current = candidate;
                            break;
                        }
                        newName = GetRandomFileNameLike(current);
                        candidate = Path.Combine(dir, newName + ext);
                    }
                }

                TryNormalizeAttributes(current);
                return current;
            }
            catch
            {
                return filePath;
            }
        }

        /// <summary>
        /// Preserves legacy behavior: randomized rename without returning the new path.
        /// Prefer <see cref="RandomRenameAndGetFinalPath(string)"/> inside this class.
        /// </summary>
        private static void RandomRenameSafe(string filePath)
        {
            _ = RandomRenameAndGetFinalPath(filePath);
        }

        /// <summary>Generates a random filename inspired by the original name length.</summary>
        private static string GetRandomFileNameLike(string originalPath)
        {
            string originalName = Path.GetFileNameWithoutExtension(originalPath);
            int len = Math.Clamp(originalName.Length, 6, 32);

            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var bytes = new byte[len];
            RandomNumberGenerator.Fill(bytes);

            var chars = new char[len];
            for (int i = 0; i < len; i++)
                chars[i] = alphabet[bytes[i] % alphabet.Length];

            return new string(chars);
        }

        /// <summary>
        /// Robust delete with attribute reset, brief exclusive-access wait, retries, and delete-on-reboot fallback.
        /// </summary>
        private static bool RobustDelete(string path)
        {
            const int maxRetries = 5;
            const int retryDelayMs = 80;

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    TryNormalizeAttributes(path);

                    // Try to gain exclusive access briefly; helps with AV/compressor races
                    if (!WaitForExclusiveAccess(path, timeoutMs: attempt == 0 ? 600 : 1200))
                    {
                        // still proceed to delete; the file may already be gone
                    }

                    File.Delete(path);
                    if (!File.Exists(path)) return true;
                }
                catch (UnauthorizedAccessException)
                {
                    TryNormalizeAttributes(path);
                }
                catch (IOException)
                {
                    // likely still in use; wait & retry
                }

                Thread.Sleep(retryDelayMs);
            }

            // Final fallback: schedule for delete on reboot
            MoveFileEx(path, null, MOVEFILE_DELAY_UNTIL_REBOOT);
            return !File.Exists(path);
        }

        /// <summary>
        /// Attempts to open a file with <see cref="FileShare.None"/> until timeout to confirm exclusive access.
        /// </summary>
        private static bool WaitForExclusiveAccess(string path, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (!File.Exists(path)) return true;
                try
                {
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                        return true;
                }
                catch
                {
                    Thread.Sleep(30);
                }
            }
            return false;
        }

        /// <summary>
        /// Best-effort directory delete with retries when empty.
        /// </summary>
        private static void TryDeleteDirectory(string dir)
        {
            const int maxTries = 3;
            for (int attempt = 1; attempt <= maxTries; attempt++)
            {
                try
                {
                    if (Directory.GetFileSystemEntries(dir).Length == 0)
                    {
                        Directory.Delete(dir, false);
                        return;
                    }
                }
                catch (IOException) when (attempt < maxTries)
                {
                    Thread.Sleep(60 * attempt);
                }
                catch (UnauthorizedAccessException) when (attempt < maxTries)
                {
                    TryNormalizeDirectoryAttributes(dir);
                    Thread.Sleep(60 * attempt);
                }
            }
        }
    }
}
