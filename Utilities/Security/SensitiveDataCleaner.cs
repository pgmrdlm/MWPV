using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using Utilities.Helpers;

namespace Utilities.Security
{
    public static class SensitiveDataCleaner
    {

        // 🔹 Clear WPF UI elements (fully qualified to avoid ambiguity)
        public static void Clear(System.Windows.Controls.TextBox tb) => tb?.Clear();
        public static void Clear(System.Windows.Controls.PasswordBox pb) => pb?.Clear();

        // 🔹 Overwrite then wipe SecureString
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

        // 🔹 Overwrite then wipe char[]

        public static void WipeCharArray(char[] data)
        {
            if (data == null || data.Length == 0) return;

            try
            {
                // Fill with cryptographically strong random chars
                char[] overwritePattern = new char[data.Length];
                byte[] randomBytes = new byte[data.Length * sizeof(char)];
                RandomNumberGenerator.Fill(randomBytes);
                Buffer.BlockCopy(randomBytes, 0, overwritePattern, 0, randomBytes.Length);

                // Overwrite original with the random pattern
                for (int i = 0; i < data.Length; i++)
                    data[i] = overwritePattern[i];

                // Clear the pattern
                Array.Clear(overwritePattern, 0, overwritePattern.Length);

                // Clear the original array
                Array.Clear(data, 0, data.Length);
            }
            catch (Exception ex)
            {
                // Uncomment when ready to log wipe failures
                // ErrorHandler.Abend(ex, "WipeCharArray failed", stage: "wipe-char-array");

                throw; // Keep rethrow so higher-level cleanup knows it failed
            }
        }
        public static void WipeCharArray(ref char[] data)
        {
            if (data == null || data.Length == 0) return;
            WipeCharArray(data); // calls your existing non-ref version
            data = null;         // drop the reference
        }

        public static void WipeByteArray(byte[] data)
        {
            if (data == null || data.Length == 0) return;

            try
            {
                // Fill with cryptographically strong random bytes
                RandomNumberGenerator.Fill(data);

                // Clear the array
                Array.Clear(data, 0, data.Length);
            }
            catch (Exception ex)
            {
                // Uncomment when ready to log wipe failures
                // ErrorHandler.Abend(ex, "WipeByteArray failed", stage: "wipe-byte-array");

                throw; // Keep rethrow so higher-level cleanup knows it failed
            }
        }



        // 🔹 Overwrite then nullify string
        public static void WipeString(ref string data)
        {
            try
            {
                if (data == null || data.Length == 0) return;

                char[] chars = data.ToCharArray();
                WipeCharArray(chars); // Secure overwrite of the character array
                data = null;

                // // Optional: Log successful wipe (enable later if desired)
                // Utilities.Helpers.ErrorHandler.Info(
                //     $"String of length {chars.Length} securely wiped.",
                //     stage: "wipe-string");
            }
            catch (Exception ex)
            {
                // Log the failure without exposing the actual string contents
                ErrorHandler.Abend(
                    ex,
                    "Failed to securely wipe string",
                    stage: "wipe-string-error",
                    severity: Utilities.Helpers.ErrorSeverity.Error
                );
            }
        }


        public static void SecureDeleteAllFiles(string folderPath, int overwritePasses = 1)
        {
            string stage = "init";
            try
            {
                stage = "check-exists";
                if (!Directory.Exists(folderPath))
                {
                    System.Diagnostics.Debug.WriteLine($"Folder does not exist: {folderPath}");
                    // // Uncomment to log missing folder
                    // ErrorHandler.Abend(new DirectoryNotFoundException($"Folder not found: {folderPath}"),
                    //     "SecureDeleteAllFiles: folder missing", stage: stage, severity: ErrorSeverity.Warning);
                    return;
                }

                stage = "enumerate";
                string[] files = Directory.GetFiles(folderPath);

                for (int i = 0; i < files.Length; i++)
                {
                    string file = files[i];
                    stage = $"delete:{Path.GetFileName(file)}";

                    bool success = SecureFileDelete(file, overwritePasses);

                    if (!success)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to securely delete: {file}");
                        // // Uncomment to log individual file failures (could be noisy if many)
                        // ErrorHandler.Abend(new IOException($"Secure delete failed for {file}"),
                        //     "SecureDeleteAllFiles: file failed", stage: stage, severity: ErrorSeverity.Error);
                    }
                }

                stage = "done";
            }
            catch (Exception ex)
            {
                // One popup for unexpected errors in the routine itself
                ErrorHandler.Abend(ex, "SecureDeleteAllFiles failed", stage: stage, severity: ErrorSeverity.Error);
            }
        }

        // 🔥 Securely overwrite and delete file (supports multi-pass)

        public static bool SecureFileDelete(string filePath, int overwritePasses = 1)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return true;
            if (!File.Exists(filePath)) return true;
            if (overwritePasses < 1) overwritePasses = 1;

            string stage = "init";
            byte[] buffer = null;

            try
            {
                stage = "normalize-attrs";
                try
                {
                    // Clear ReadOnly/Hidden/System in one go
                    File.SetAttributes(filePath, FileAttributes.Normal);
                }
                catch { /* non-fatal; continue */ }

                stage = "stat-file";
                long length = new FileInfo(filePath).Length;

                stage = "alloc-buffer";
                buffer = new byte[4096];

                stage = "open-stream";
                using (var stream = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    options: FileOptions.WriteThrough | FileOptions.SequentialScan))
                {
                    for (int pass = 0; pass < overwritePasses; pass++)
                    {
                        stage = $"overwrite-pass-{pass}-seek";
                        stream.Seek(0, SeekOrigin.Begin);

                        long remaining = length;
                        stage = $"overwrite-pass-{pass}-loop";
                        while (remaining > 0)
                        {
                            RandomNumberGenerator.Fill(buffer);
                            int toWrite = (int)Math.Min(buffer.Length, remaining);
                            stream.Write(buffer, 0, toWrite);
                            remaining -= toWrite;
                        }
                    }

                    stage = "flush-fsync";
#if NET6_0_OR_GREATER
                    stream.Flush(true);
#else
            stream.Flush();
#endif
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

                stage = "delete-file";
                // retry delete a few times in case another process briefly grabs it
                const int maxTries = 3;
                for (int attempt = 1; attempt <= maxTries; attempt++)
                {
                    try
                    {
                        File.Delete(filePath);
                        break;
                    }
                    catch (IOException) when (attempt < maxTries)
                    {
                        System.Threading.Thread.Sleep(50 * attempt);
                        continue;
                    }
                    catch (UnauthorizedAccessException) when (attempt < maxTries)
                    {
                        System.Threading.Thread.Sleep(50 * attempt);
                        continue;
                    }
                }

                stage = "verify-deletion";
                if (File.Exists(filePath))
                    throw new IOException("File still exists after secure delete attempts.");

                stage = "scrub-buffer";
                if (buffer != null) Array.Clear(buffer, 0, buffer.Length);

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


        public static void WipeSensitiveStrings(ref string password, ref string archivePath)
        {
            if (!string.IsNullOrEmpty(password))
            {
                // Overwrite with randomized data (cosmetic only)
                password = SecurePassword.GenerateAsString(password.Length);
                password = null;
            }

            archivePath = null;
        }
    }
}
