using SevenZip;
using System;
using System.IO;
using System.Linq;
using System.Text;

using Utilities.Helpers;   // ErrorHandler
using Security.Utility;  // SecureEncryptedDataStore, SensitiveDataCleaner
using Utilities.Sql;       // DatabaseHelper

namespace Security.Utility
{
    /// <summary>
    /// First-run provisioning and secure loading utilities.
    /// Responsibilities:
    ///   • Create local MWPV folder + encrypted DB from schema.
    ///   • Build the encrypted key archive from the SQL staging folder.
    ///   • Ensure app keyset exists (via KeyProvisioner) and is loaded.
    ///   • Read/write individual files in the encrypted archive.
    /// Security:
    ///   • DB password staged to %TEMP% under the canonical file name, then packed and securely deleted.
    ///   • SQL staging folder files are securely wiped, then the folder is removed (name shredding).
    /// </summary>
    internal class ServiceSetUp
    {
        #region Constants / Keys

        private const string Key_KeyPW = "KeyPW";              // char[]
        private const string Key_KeyFile = "KeyFile";            // string (path)
        private const string Key_DbPwPath = "DB_Password_Path";   // string (temp plaintext path)
        private const string Key_DbConnNoPw = "DB_String";          // string (conn string without pw)

        private static string LocalRoot =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MWPV");

        private static string SqlFolder => Path.Combine(LocalRoot, "sql");
        private static string DbPath => Path.Combine(LocalRoot, "MWPV.db");

        #endregion

        #region Database setup

        /// <summary>
        /// Ensures local root + SQL staging folder exist, initializes DB from
        /// <c>.../sql/MWPV_DB_Create.sql</c>, stages the DB password to %TEMP% under the
        /// canonical filename (for packing), and caches the passwordless connection string.
        /// </summary>
        /// <returns>Local root path on success; "error" on failure.</returns>
        public string SetUpDataBase()
        {
            try
            {
                Directory.CreateDirectory(LocalRoot);
                Directory.CreateDirectory(SqlFolder); // created here; later wiped by SetUpKeyFile()
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Unable to create MWPV local folders.");
                return "error";
            }

            string schemaSql = null!;
            try
            {
                var schemaPath = Path.Combine(SqlFolder, "MWPV_DB_Create.sql");
                schemaSql = File.ReadAllText(schemaPath, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Unable to read schema file.");
                return "error";
            }

            try
            {
                using (var cn = DatabaseHelper.OpenConnection())
                using (var cmd = cn.CreateCommand())
                {
                    cmd.CommandText = schemaSql;
                    cmd.ExecuteNonQuery();
                }

                // Stage DB password to %TEMP% using canonical filename so it’s included in the archive.
                var tempPwPath = Path.Combine(Path.GetTempPath(), DatabaseHelper.DbPasswordKey);
                try
                {
                    DatabaseHelper.WithDatabasePasswordString(pw =>
                    {
                        File.WriteAllText(tempPwPath, pw, Encoding.UTF8);
                        SecureEncryptedDataStore.SetString(Key_DbPwPath, tempPwPath);
                    });
                }
                finally
                {
                    SensitiveDataCleaner.WipeString(ref tempPwPath);
                }

                SecureEncryptedDataStore.SetString(Key_DbConnNoPw, $"Data Source={DbPath}");
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error creating database file.");
                return "error";
            }
            finally
            {
                SensitiveDataCleaner.WipeString(ref schemaSql);
            }

            return LocalRoot;
        }

        #endregion

        #region Archive build (staging -> encrypted 7z)

        /// <summary>
        /// Builds (or rebuilds) the encrypted key archive from the plain <c>.../sql</c> folder.
        /// Steps:
        ///   1) Copy staged DB password from %TEMP% into staging (canonical file name).
        ///   2) Ensure keyset.json exists in staging (and load keys).
        ///   3) Securely delete any existing archive.
        ///   4) Create new encrypted archive (AES-256, header encryption).
        ///   5) Securely wipe all files in staging, then remove the directory (name shredding).
        /// </summary>
        /// <returns>Success message with archive path, or "Error ..." text.</returns>
        public string SetUpKeyFile()
        {
            string archivePath = SecureEncryptedDataStore.GetString(Key_KeyFile);

            char[] pwChars = null;
            string pw = null;
            try
            {
                pwChars = SecureEncryptedDataStore.GetChars(Key_KeyPW);
                pw = new string(pwChars ?? Array.Empty<char>());
            }
            finally
            {
                if (pwChars != null) SensitiveDataCleaner.WipeCharArray(pwChars);
            }

            try
            {
                if (string.IsNullOrWhiteSpace(archivePath) || string.IsNullOrWhiteSpace(pw))
                    return "Missing KeyFile path or password.";

                if (!Directory.Exists(SqlFolder))
                    return "The source directory to compress does not exist.";

                // 1) Bring staged DB password into the folder so it gets packed.
                var tempPw = SecureEncryptedDataStore.GetString(Key_DbPwPath);
                if (!string.IsNullOrWhiteSpace(tempPw) && File.Exists(tempPw))
                {
                    File.Copy(tempPw, Path.Combine(SqlFolder, DatabaseHelper.DbPasswordKey), overwrite: true);
                    SensitiveDataCleaner.SecureFileDelete(tempPw);
                }

                // 2) Ensure keyset.json exists (and load keys to SEDS) before zipping.
                EnsureKeySetForFirstRun_Folder(SqlFolder);

                // 3) Securely remove any older archive (best-effort).
                if (File.Exists(archivePath))
                {
                    SensitiveDataCleaner.SecureFileDelete(
                        archivePath,
                        overwritePasses: 1,
                        shredName: true,
                        finalZeroPass: true);
                }

                // 4) Build encrypted archive from staging files.
                var files = Directory.GetFiles(SqlFolder, "*.*", SearchOption.TopDirectoryOnly);
                if (files.Length == 0) return "No files found to compress.";

                var compressor = new SevenZipCompressor
                {
                    ArchiveFormat = OutArchiveFormat.SevenZip,
                    CompressionLevel = CompressionLevel.Ultra,
                    CompressionMethod = CompressionMethod.Lzma2,
                    EncryptHeaders = true,
                    ZipEncryptionMethod = ZipEncryptionMethod.Aes256,
                    PreserveDirectoryRoot = true
                };
                compressor.CompressFilesEncrypted(archivePath, pw, files);

                // 5) Wipe staging files and remove the folder (name shredding).
                SecurelyScrubSqlStagingFolder();

                return $"Encrypted archive created at: {archivePath}";
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error creating archive.");
                // Even on failure, try to scrub staging so we don’t strand secrets.
                try { SecurelyScrubSqlStagingFolder(); } catch { /* best-effort */ }
                return "Error creating archive: " + ex.Message;
            }
            finally
            {
                SensitiveDataCleaner.WipeString(ref pw);
                archivePath = null;
            }
        }

        private static void SecurelyScrubSqlStagingFolder()
        {
            if (!Directory.Exists(SqlFolder)) return;

            // Wipe file contents first…
            SensitiveDataCleaner.SecureDeleteAllFiles(SqlFolder, overwritePasses: 3);

            // …then remove the directory (shred names, remove dirs).
            try
            {
                SensitiveDataCleaner.SecureDeleteDirectory(
                    SqlFolder,
                    overwritePasses: 1,
                    shredNames: true,
                    finalZeroPass: false,
                    removeDirectories: true);
            }
            catch { /* best-effort */ }
        }

        #endregion

        #region Archive read helpers

        /// <summary>
        /// Compatibility wrapper around <see cref="KeyArchiveVerifier.VerifyPasswordAndSentinels"/>.
        /// </summary>
        [Obsolete("Use KeyArchiveVerifier.VerifyPasswordAndSentinels(...) instead.")]
        public static bool VerifyKeyFilePW(string archivePath, string password)
            => KeyArchiveVerifier.VerifyPasswordAndSentinels(archivePath, password);

        /// <summary>
        /// Load a text file (SQL or DB password file) from the encrypted key archive into SEDS.
        /// Special-case: if the file name equals <see cref="DatabaseHelper.DbPasswordKey"/>,
        /// the password is loaded via <see cref="DatabaseHelper.StoreDatabasePassword(char[])"/>.
        /// </summary>
        /// <param name="fileNameInArchive">Case-insensitive; base name allowed.</param>
        /// <returns>"worked", "not_found", or "error".</returns>
        public static string LoadSqlFromEncryptedArchive(string fileNameInArchive)
        {
            try
            {
                var archive = SecureEncryptedDataStore.GetString(Key_KeyFile);

                char[] pwChars = null;
                string pw = null;
                try
                {
                    pwChars = SecureEncryptedDataStore.GetChars(Key_KeyPW);
                    pw = new string(pwChars ?? Array.Empty<char>());
                }
                finally
                {
                    if (pwChars != null) SensitiveDataCleaner.WipeCharArray(pwChars);
                }

                using var extractor = new SevenZipExtractor(archive, pw);

                var entry = extractor.ArchiveFileData.FirstOrDefault(f =>
                    string.Equals(f.FileName, fileNameInArchive, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileName(f.FileName), fileNameInArchive, StringComparison.OrdinalIgnoreCase));

                if (entry.FileName == null || entry.IsDirectory)
                {
                    SecureEncryptedDataStore.Clear(fileNameInArchive);
                    return "not_found";
                }

                using var ms = new MemoryStream();
                extractor.ExtractFile(entry.Index, ms);
                ms.Position = 0;

                using var reader = new StreamReader(ms, Encoding.UTF8);
                string contents = reader.ReadToEnd();

                try
                {
                    if (string.Equals(Path.GetFileName(entry.FileName),
                                      DatabaseHelper.DbPasswordKey,
                                      StringComparison.OrdinalIgnoreCase))
                    {
                        DatabaseHelper.StoreDatabasePassword(contents.ToCharArray());
                        return "worked";
                    }

                    SecureEncryptedDataStore.SetString(fileNameInArchive, contents);
                    return "worked";
                }
                finally
                {
                    SensitiveDataCleaner.WipeString(ref contents);
                    SensitiveDataCleaner.WipeString(ref pw);
                }
            }
            catch (SevenZipException)
            {
                return "error"; // invalid archive / wrong password
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Unexpected error loading from archive.");
                return "error";
            }
        }

        /// <summary>
        /// Ensure <c>keyset.json</c> exists inside the encrypted archive and load keys into SEDS.
        /// Call after verifying the archive password.
        /// </summary>
        public static void EnsureKeySetFromArchive()
        {
            KeyProvisioner.EnsureKeySetLoaded(
                loadKeyset: () => LoadKeyFromArchiveAsBytes("keyset.json"),
                saveKeyset: bytes => SaveKeyToArchive("keyset.json", bytes)
            );
        }

        #endregion

        #region KeyProvisioner glue for first-run (plain folder)

        /// <summary>
        /// Ensure <c>keyset.json</c> exists in the plain SQL folder and load keys into SEDS.
        /// Invoked before zipping so the archive includes the keyset.
        /// </summary>
        private static void EnsureKeySetForFirstRun_Folder(string folder)
        {
            KeyProvisioner.EnsureKeySetLoaded(
                loadKeyset: () => ReadBytesFromFolder(folder, "keyset.json"),
                saveKeyset: bytes => WriteBytesToFolder(folder, "keyset.json", bytes)
            );
        }

        #endregion

        #region Low-level helpers

        private static byte[] LoadKeyFromArchiveAsBytes(string name)
        {
            var archive = SecureEncryptedDataStore.GetString(Key_KeyFile);
            if (string.IsNullOrWhiteSpace(archive) || !File.Exists(archive)) return null;

            char[] pwChars = null;
            string pw = null;
            try
            {
                pwChars = SecureEncryptedDataStore.GetChars(Key_KeyPW);
                pw = new string(pwChars ?? Array.Empty<char>());

                using var extractor = new SevenZipExtractor(archive, pw);
                var entry = extractor.ArchiveFileData.FirstOrDefault(f =>
                    string.Equals(f.FileName, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileName(f.FileName), name, StringComparison.OrdinalIgnoreCase));

                if (entry.FileName == null || entry.IsDirectory) return null;

                using var ms = new MemoryStream();
                extractor.ExtractFile(entry.Index, ms);
                return ms.ToArray();
            }
            catch
            {
                return null;
            }
            finally
            {
                if (pwChars != null) SensitiveDataCleaner.WipeCharArray(pwChars);
                SensitiveDataCleaner.WipeString(ref pw);
            }
        }

        private static void SaveKeyToArchive(string name, byte[] bytes)
        {
            var archive = SecureEncryptedDataStore.GetString(Key_KeyFile);
            if (string.IsNullOrWhiteSpace(archive))
                throw new InvalidOperationException("Key file path is not set.");

            var tempDir = Path.Combine(Path.GetTempPath(), "MWPV_repack_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            char[] pwChars = null;
            string pw = null;
            try
            {
                pwChars = SecureEncryptedDataStore.GetChars(Key_KeyPW);
                pw = new string(pwChars ?? Array.Empty<char>());

                if (File.Exists(archive))
                {
                    using var extractor = new SevenZipExtractor(archive, pw);
                    for (int i = 0; i < extractor.ArchiveFileData.Count; i++)
                    {
                        var e = extractor.ArchiveFileData[i];
                        if (e.IsDirectory) continue;

                        var outPath = Path.Combine(tempDir, Path.GetFileName(e.FileName));
                        using var fs = File.Create(outPath);
                        extractor.ExtractFile(i, fs);
                    }
                }

                File.WriteAllBytes(Path.Combine(tempDir, name), bytes);

                var compressor = new SevenZipCompressor
                {
                    ArchiveFormat = OutArchiveFormat.SevenZip,
                    CompressionLevel = CompressionLevel.Ultra,
                    CompressionMethod = CompressionMethod.Lzma2,
                    EncryptHeaders = true,
                    ZipEncryptionMethod = ZipEncryptionMethod.Aes256,
                    PreserveDirectoryRoot = true
                };
                var files = Directory.GetFiles(tempDir, "*.*", SearchOption.TopDirectoryOnly);
                compressor.CompressFilesEncrypted(archive, pw, files);
            }
            finally
            {
                try
                {
                    SensitiveDataCleaner.SecureDeleteDirectory(
                        tempDir,
                        overwritePasses: 1,
                        shredNames: true,
                        finalZeroPass: false,
                        removeDirectories: true);
                }
                catch { /* best-effort */ }

                if (pwChars != null) SensitiveDataCleaner.WipeCharArray(pwChars);
                SensitiveDataCleaner.WipeString(ref pw);
            }
        }

        private static byte[] ReadBytesFromFolder(string folder, string name)
        {
            try
            {
                var path = Path.Combine(folder, name);
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            }
            catch { return null; }
        }

        private static void WriteBytesToFolder(string folder, string name, byte[] data)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, name), data);
        }

        #endregion
    }
}
