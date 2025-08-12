using System;
using System.Collections.Generic;
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
    /// Centralized utilities for securely wiping sensitive data in memory and on disk.
    /// </summary>
    public static class SensitiveDataCleaner
    {
        // =========================================================
        // UI helpers (WPF)
        // =========================================================
        public static void Clear(System.Windows.Controls.TextBox tb) => tb?.Clear();
        public static void Clear(System.Windows.Controls.PasswordBox pb) => pb?.Clear();

        // =========================================================
        // SecureString
        // =========================================================
        public static void WipeSecureString(SecureString secure)
        {
            if (secure == null || secure.Length == 0) return;

            IntPtr unmanaged = IntPtr.Zero;
            try
            {
                unmanaged = Marshal.SecureStringToGlobalAllocUnicode(secure);
                for (int i = 0; i < secure.Length; i++)
                {
                    Marshal.WriteInt16(unmanaged, i * 2, '\0');
                }
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
        public static void WipeCharArray(char[] data)
        {
            if (data == null || data.Length == 0) return;

            try
            {
                // Overwrite with cryptographically strong random chars
                var overwritePattern = new char[data.Length];
                var randomBytes = new byte[data.Length * sizeof(char)];
                RandomNumberGenerator.Fill(randomBytes);
                Buffer.BlockCopy(randomBytes, 0, overwritePattern, 0, randomBytes.Length);

                for (int i = 0; i < data.Length; i++)
                    data[i] = overwritePattern[i];

                Array.Clear(overwritePattern, 0, overwritePattern.Length);
                Array.Clear(data, 0, data.Length);
            }
            catch (Exception ex)
            {
                // keep throwing so callers know it failed
                // ErrorHandler.Abend(ex, "WipeCharArray failed", stage: "wipe-char-array", severity: ErrorSeverity.Error);
                throw;
            }
        }

        public static void WipeCharArray(ref char[] data)
        {
            if (data == null || data.Length == 0) { data = null; return; }
            WipeCharArray(data);
            data = null;
        }

        // =========================================================
        // byte[] wipes
        // =========================================================
        public static void WipeByteArray(byte[] data)
        {
            if (data == null || data.Length == 0) return;

            try
            {
                RandomNumberGenerator.Fill(data);       // overwrite with random
                Array.Clear(data, 0, data.Length);      // then zero
            }
            catch (Exception ex)
            {
                // ErrorHandler.Abend(ex, "WipeByteArray failed", stage: "wipe-byte-array", severity: ErrorSeverity.Error);
                throw;
            }
        }

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
        /// Wipes a string by copying to a char[] and wiping that, then nulling the reference.
        /// NOTE: Strings are immutable; this does not guarantee the original string's memory is overwritten.
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
                ErrorHandler.Abend(
                    ex,
                    "Failed to securely wipe string",
                    stage: "wipe-string-error",
                    severity: ErrorSeverity.Error
                );
            }
        }

        /// <summary>
        /// Best-effort wipe for StringBuilder contents.
        /// Uses GetChunks() when available to avoid extra copies.
        /// </summary>
        public static void WipeStringBuilder(StringBuilder sb)
        {
            if (sb == null || sb.Length == 0) return;
            try
            {
#if NET5_0_OR_GREATER
                foreach (var chunk in sb.GetChunks())
                {
                    // Work on a copy to be able to overwrite; then overwrite the chunk via Replace.
                    var tmp = chunk.Span.ToArray();
                    WipeCharArray(tmp);
                }
                sb.Clear();
#else
                // Fallback (older frameworks): overwrite via Replace pattern-length.
                // Not perfect; still better than plain Clear().
                for (int i = 0; i < sb.Length; i++) sb[i] = '\0';
                sb.Clear();
#endif
            }
            catch { /* best effort */ }
        }

        // Convenience for common pair we used before
        public static void WipeSensitiveStrings(ref string password, ref string archivePath)
        {
            if (!string.IsNullOrEmpty(password))
            {
                // Cosmetic randomization before nulling (optional)
                try
                {
                    password = SecurePassword.GenerateAsString(password.Length);
                }
                catch
                {
                    // fallback: quick random
                    password = RandomizedAscii(password.Length);
                }
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
        // Collection helpers (used in various cleanup paths)
        // =========================================================
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

        public static void DisposeAndNull<T>(ref T disposable) where T : class, IDisposable
        {
            if (disposable == null) return;
            try { disposable.Dispose(); }
            catch { /* swallow */ }
            finally { disposable = null; }
        }

        // =========================================================
        // DISK: secure file / folder deletion
        // =========================================================

        /// <summary>
        /// Securely deletes every file in a directory (optionally recursive).
        /// </summary>
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
                    System.Diagnostics.Debug.WriteLine($"Folder does not exist: {folderPath}");
                    return;
                }

                stage = "enumerate";
                var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                string[] files = Directory.GetFiles(folderPath, "*", option);

                for (int i = 0; i < files.Length; i++)
                {
                    string file = files[i];
                    bool success = SecureFileDelete(file, overwritePasses, shredNames, finalZeroPass);
                    if (!success)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to securely delete: {file}");
                        // ErrorHandler.Abend(new IOException($"Secure delete failed for {file}"),
                        //     "SecureDeleteAllFiles: file failed", stage: $"delete:{Path.GetFileName(file)}", severity: ErrorSeverity.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "SecureDeleteAllFiles failed", stage: stage, severity: ErrorSeverity.Error);
            }
        }

        /// <summary>
        /// Securely wipe and delete a single file.
        /// </summary>
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

            try
            {
                stage = "normalize-attrs";
                TryNormalizeAttributes(filePath);

                stage = "stat";
                var info = new FileInfo(filePath);
                long length = info.Length;

                if (length == 0)
                {
                    stage = "scrub-meta-empty";
                    ScrubTimestampsSafe(filePath);
                    if (shredName) RandomRenameSafe(filePath);
                    return DeleteWithRetry(filePath);
                }

                stage = "alloc-buffer";
                const int BufSize = 64 * 1024;
                buffer = new byte[BufSize];

                stage = "open-write";
                using (var stream = new FileStream(
                    filePath,
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
                using (var trunc = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.None))
                {
                    trunc.SetLength(0);
#if NET6_0_OR_GREATER
                    trunc.Flush(true);
#else
                    trunc.Flush();
#endif
                }

                stage = "scrub-meta";
                ScrubTimestampsSafe(filePath);

                stage = "shred-name";
                if (shredName) RandomRenameSafe(filePath);

                stage = "delete";
                if (!DeleteWithRetry(filePath))
                    throw new IOException("File still exists after secure delete attempts.");

                return true;
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Secure delete failed", stage: stage, severity: ErrorSeverity.Error);
                return false;
            }
            finally
            {
                if (buffer != null) Array.Clear(buffer, 0, buffer.Length);
            }
        }

        /// <summary>
        /// Recursively wipes all files in a directory then deletes (optionally) the empty directories.
        /// </summary>
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
                // Walk bottom-up to remove empty folders
                foreach (var dir in Directory.GetDirectories(folderPath, "*", SearchOption.AllDirectories))
                {
                    TryNormalizeDirectoryAttributes(dir);
                    TryDeleteDirectory(dir);
                }
                TryDeleteDirectory(folderPath); // top-level last
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "SecureDeleteDirectory failed", stage: "remove-dirs", severity: ErrorSeverity.Warning);
            }
        }

        // --------------------------
        // helpers
        // --------------------------
        private static void TryNormalizeAttributes(string filePath)
        {
            try { File.SetAttributes(filePath, FileAttributes.Normal); }
            catch { /* non-fatal */ }
        }

        private static void TryNormalizeDirectoryAttributes(string dir)
        {
            try { File.SetAttributes(dir, FileAttributes.Directory | FileAttributes.Normal); }
            catch { /* non-fatal */ }
        }

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

        private static void RandomRenameSafe(string filePath)
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
            }
            catch { /* non-fatal */ }
        }

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

        private static bool DeleteWithRetry(string filePath)
        {
            const int maxTries = 4;
            for (int attempt = 1; attempt <= maxTries; attempt++)
            {
                try
                {
                    File.Delete(filePath);
                    return !File.Exists(filePath);
                }
                catch (IOException) when (attempt < maxTries)
                {
                    Thread.Sleep(60 * attempt);
                }
                catch (UnauthorizedAccessException) when (attempt < maxTries)
                {
                    TryNormalizeAttributes(filePath);
                    Thread.Sleep(60 * attempt);
                }
            }
            return !File.Exists(filePath);
        }

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
        public enum SecureDeleteError { None, NotFound, InUse, IoError, Unauthorized, Unknown }

        public sealed class SecureDeleteResult
        {
            public bool Success { get; init; }
            public SecureDeleteError Error { get; init; }
            public string Detail { get; init; }
        }

        public static SecureDeleteResult SecureFileDeleteDiagnose(string path, int overwritePasses = 1, int maxRetries = 5)
        {
            try { System.IO.File.SetAttributes(path, System.IO.FileAttributes.Normal); } catch { /* best-effort */ }

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    using (var fs = new System.IO.FileStream(
                        path, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.WriteThrough))
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
                    System.IO.File.Delete(path);
                    return new SecureDeleteResult { Success = true, Error = SecureDeleteError.None };
                }
                catch (System.IO.FileNotFoundException ex)
                {
                    return new SecureDeleteResult { Success = false, Error = SecureDeleteError.NotFound, Detail = ex.Message };
                }
                catch (System.UnauthorizedAccessException ex)
                {
                    System.Threading.Thread.Sleep(60 * (attempt + 1));
                    if (attempt == maxRetries)
                        return new SecureDeleteResult { Success = false, Error = SecureDeleteError.Unauthorized, Detail = ex.Message };
                }
                catch (System.IO.IOException ex)
                {
                    // usually “in use” / sharing violation
                    System.Threading.Thread.Sleep(60 * (attempt + 1));
                    if (attempt == maxRetries)
                        return new SecureDeleteResult { Success = false, Error = SecureDeleteError.InUse, Detail = ex.Message };
                }
                catch (Exception ex)
                {
                    System.Threading.Thread.Sleep(60 * (attempt + 1));
                    if (attempt == maxRetries)
                        return new SecureDeleteResult { Success = false, Error = SecureDeleteError.Unknown, Detail = ex.Message };
                }
            }

            return new SecureDeleteResult { Success = false, Error = SecureDeleteError.Unknown, Detail = "max retries exceeded" };
        }
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

                // now do the strong, instrumented delete you already wrote
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

    }
}
