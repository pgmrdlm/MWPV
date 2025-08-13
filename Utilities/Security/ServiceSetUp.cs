using Microsoft.Data.Sqlite;
using SevenZip;
using System;
using System.IO;
using System.Linq;
using System.Text;
using Utilities.Helpers;
using Utilities.Security;
using Utilities.Sql;

namespace Utilities.Security
{
    /// <summary>
    /// First-run provisioning and ongoing secure loading utilities:
    /// - Creates the local MWPV folder + encrypted DB from schema.
    /// - Builds the encrypted 7z key archive from the SQL folder.
    /// - Centralizes keyset creation/loading via <see cref="KeyProvisioner"/>.
    /// </summary>
    internal class ServiceSetUp
    {
        private const string Key_KeyPW = "KeyPW";
        private const string Key_KeyFile = "KeyFile";
        private const string Key_DbPwPath = "DB_Password_Path";
        private const string Key_DbConnNoPw = "DB_String";

        // NOTE: ensure this path is valid on the target machine
        private static readonly string sevenZipLibraryPath = @"C:\Users\pgmrd\My Drive\MWPV\MWPV\7z.dll";

        /// <summary>
        /// Creates the local MWPV folder and initializes the encrypted database from schema.
        /// Also writes a temp plaintext copy of the DB password (canonical filename) so the key archive can pack it.
        /// </summary>
        /// <returns>Root MWPV folder path, or "error" on failure.</returns>
        public string SetUpDataBase()
        {
            string strMWPV_Folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MWPV"
            );

            try
            {
                if (!Directory.Exists(strMWPV_Folder))
                    Directory.CreateDirectory(strMWPV_Folder);
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Unable to create MWPV local folder.");
                return "error";
            }

            string strMWPV_DB = Path.Combine(strMWPV_Folder, "MWPV.db");
            string strDB_Create = null;

            try
            {
                var schemaPath = Path.Combine(strMWPV_Folder, "sql", "MWPV_DB_Create.sql");
                strDB_Create = File.ReadAllText(schemaPath, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Unable to read schema file.");
                return "error";
            }

            try
            {
                using (var conn = DatabaseHelper.GetAppOpenConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = strDB_Create;
                    cmd.ExecuteNonQuery();
                }

                // Temp location for the DB password under the canonical filename (consumed by SetUpKeyFile)
                string tempPasswordPath = Path.Combine(Path.GetTempPath(), DatabaseHelper.DbPasswordKey);
                try
                {
                    DatabaseHelper.WithDatabasePasswordString(pw =>
                    {
                        File.WriteAllText(tempPasswordPath, pw, Encoding.UTF8);
                        SecureEncryptedDataStore.SetString(Key_DbPwPath, tempPasswordPath);
                    });
                }
                finally
                {
                    SensitiveDataCleaner.WipeString(ref tempPasswordPath);
                }

                SecureEncryptedDataStore.SetString(Key_DbConnNoPw, $"Data Source={strMWPV_DB}");
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error creating database file.");
                return "error";
            }
            finally
            {
                SensitiveDataCleaner.WipeString(ref strDB_Create);
            }

            return strMWPV_Folder;
        }

        /// <summary>
        /// Builds/overwrites the encrypted key archive (7z) from the SQL folder.
        /// Before compressing, ensures <c>keyset.json</c> exists (via <see cref="KeyProvisioner"/>) so the archive contains the full keyset.
        /// After compressing, securely deletes the folder contents.
        /// </summary>
        /// <returns>Success message with archive path, or "error: ..." on failure.</returns>
        public string SetUpKeyFile()
        {
            string strDirectoryToCompress = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MWPV", "sql"
            );

            string strKeyFilePath = SecureEncryptedDataStore.GetString(Key_KeyFile);

            // Key archive password (from secure store)
            char[] keyFilePwChars = null;
            string strKeyFilePW = null;
            try
            {
                keyFilePwChars = SecureEncryptedDataStore.GetChars(Key_KeyPW);
                strKeyFilePW = new string(keyFilePwChars);
            }
            finally
            {
                if (keyFilePwChars != null) SensitiveDataCleaner.WipeCharArray(keyFilePwChars);
            }

            try
            {
                if (!File.Exists(sevenZipLibraryPath))
                    throw new FileNotFoundException("7z.dll not found at specified path.", sevenZipLibraryPath);

                if (string.IsNullOrWhiteSpace(strKeyFilePath) || string.IsNullOrWhiteSpace(strKeyFilePW))
                    return "Missing KeyFile path or password.";

                if (!Directory.Exists(strDirectoryToCompress))
                    return "The source directory to compress does not exist.";

                SevenZipBase.SetLibraryPath(sevenZipLibraryPath);

                string[] files = Directory.GetFiles(strDirectoryToCompress, "*.*", SearchOption.TopDirectoryOnly);
                if (files.Length == 0)
                    return "No files found to compress.";

                // 1) Place canonical DB password file into the SQL folder so it gets packed
                string strtmppwfile = SecureEncryptedDataStore.GetString(Key_DbPwPath);
                if (!string.IsNullOrWhiteSpace(strtmppwfile) && File.Exists(strtmppwfile))
                {
                    File.Copy(strtmppwfile, Path.Combine(strDirectoryToCompress, DatabaseHelper.DbPasswordKey), true);
                    SensitiveDataCleaner.SecureFileDelete(strtmppwfile);
                }

                // 2) Ensure the keyset exists *in the folder* before we compress (so keyset.json is included in the archive)
                EnsureKeySetForFirstRun_Folder(strDirectoryToCompress);

                // 3) If there is an older archive, best-effort secure delete it first
                if (!string.IsNullOrWhiteSpace(strKeyFilePath) && File.Exists(strKeyFilePath))
                {
                    SensitiveDataCleaner.SecureFileDelete(
                        strKeyFilePath,
                        overwritePasses: 1,
                        shredName: true,
                        finalZeroPass: true
                    );
                }

                // 4) Build encrypted archive from the folder
                var compressor = new SevenZipCompressor
                {
                    ArchiveFormat = OutArchiveFormat.SevenZip,
                    CompressionLevel = CompressionLevel.Ultra,
                    CompressionMethod = CompressionMethod.Lzma2,
                    EncryptHeaders = true,
                    ZipEncryptionMethod = ZipEncryptionMethod.Aes256,
                    PreserveDirectoryRoot = true
                };

                files = Directory.GetFiles(strDirectoryToCompress, "*.*", SearchOption.TopDirectoryOnly); // refresh (now includes keyset.json)
                compressor.CompressFilesEncrypted(strKeyFilePath, strKeyFilePW, files);

                // 5) Securely wipe the folder contents (removes plaintext keyset.json, SQL files, temp pw file etc.)
                SensitiveDataCleaner.SecureDeleteAllFiles(strDirectoryToCompress, overwritePasses: 3);

                return $"Encrypted archive created at: {strKeyFilePath}";
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error creating archive.");
                return "Error creating archive: " + ex.Message;
            }
            finally
            {
                SensitiveDataCleaner.WipeString(ref strKeyFilePW);
                strKeyFilePath = null;
            }
        }

        /// <summary>
        /// Verifies the key archive password by attempting to open the 7z and read metadata.
        /// </summary>
        /// <returns><c>true</c> for valid password/archive; <c>false</c> for wrong password or invalid/corrupt file.</returns>
        public static bool VerifyKeyFilePW(string archivePath, string password)
        {
            try
            {
                SevenZipBase.SetLibraryPath(sevenZipLibraryPath);
                using (var extractor = new SevenZipExtractor(archivePath, password))
                {
                    _ = extractor.ArchiveFileData.FirstOrDefault(); // force a read
                    return true;
                }
            }
            catch (SevenZipException) { return false; }     // wrong password or corrupt
            catch (ArgumentException) { return false; }     // invalid file type
            catch { return false; }                         // any other fatal error
            finally
            {
                SensitiveDataCleaner.WipeSensitiveStrings(ref password, ref archivePath);
            }
        }

        /// <summary>
        /// Loads a text file (e.g., SQL or the DB password file) from the encrypted key archive into the secure store.
        /// </summary>
        /// <param name="strFile">Filename inside the archive (case-insensitive; base name allowed).</param>
        /// <returns>"worked", "not_found", or "error".</returns>
        public static string LoadSqlFromEncryptedArchive(string strFile)
        {
            try
            {
                SevenZipBase.SetLibraryPath(sevenZipLibraryPath);

                string strKeyFile = SecureEncryptedDataStore.GetString(Key_KeyFile);

                char[] keyPwChars = null;
                string strKeyPW = null;
                try
                {
                    keyPwChars = SecureEncryptedDataStore.GetChars(Key_KeyPW);
                    strKeyPW = new string(keyPwChars);
                }
                finally
                {
                    if (keyPwChars != null) SensitiveDataCleaner.WipeCharArray(keyPwChars);
                }

                using var extractor = new SevenZipExtractor(strKeyFile, strKeyPW);

                var entry = extractor.ArchiveFileData.FirstOrDefault(f =>
                    string.Equals(f.FileName, strFile, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileName(f.FileName), strFile, StringComparison.OrdinalIgnoreCase));

                if (entry.FileName == null || entry.IsDirectory)
                {
                    SecureEncryptedDataStore.Clear(strFile);
                    return "not_found";
                }

                using var memStream = new MemoryStream();
                extractor.ExtractFile(entry.Index, memStream);
                memStream.Position = 0;

                using var reader = new StreamReader(memStream, Encoding.UTF8);
                string strContents = reader.ReadToEnd();

                try
                {
                    // SPECIAL-CASE: password file — use the shared constant
                    if (string.Equals(Path.GetFileName(entry.FileName), DatabaseHelper.DbPasswordKey, StringComparison.OrdinalIgnoreCase))
                    {
                        DatabaseHelper.StoreDatabasePassword(strContents.ToCharArray());
                        return "worked";
                    }

                    SecureEncryptedDataStore.SetString(strFile, strContents);
                    return "worked";
                }
                finally
                {
                    SensitiveDataCleaner.WipeString(ref strContents);
                    SensitiveDataCleaner.WipeString(ref strKeyPW);
                }
            }
            catch (SevenZipException ex)
            {
                EarlyLoginFailures.Record(EarlyFailType.KeyfileMissingOrCorrupt, ex.Message);
                return "error";
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Unexpected error loading from archive.");
                return "error";
            }
        }

        // =====================================================================
        // KeyProvisioner glue
        // =====================================================================

        /// <summary>
        /// First-run helper: ensures keyset.json exists in the plain SQL folder and loads keys into the session.
        /// Called BEFORE zipping so the archive includes keyset.json.
        /// </summary>
        private static void EnsureKeySetForFirstRun_Folder(string sqlFolder)
        {
            KeyProvisioner.EnsureKeySetLoaded(
                loadKeyset: () => ReadBytesFromFolder(sqlFolder, "keyset.json"),
                saveKeyset: bytes => WriteBytesToFolder(sqlFolder, "keyset.json", bytes)
            );
        }

        /// <summary>
        /// Future use: ensures keyset.json exists inside the encrypted archive and loads keys into the session.
        /// Typically not needed after first run unless migrating legacy archives.
        /// </summary>
        public static void EnsureKeySetFromArchive()
        {
            KeyProvisioner.EnsureKeySetLoaded(
                loadKeyset: () => LoadKeyFromArchiveAsBytes("keyset.json"),
                saveKeyset: bytes => SaveKeyToArchive("keyset.json", bytes) // rebuilds archive if needed
            );
        }

        /// <summary>Reads a file's bytes from the given plain folder; returns null if missing.</summary>
        private static byte[] ReadBytesFromFolder(string folder, string name)
        {
            try
            {
                var path = Path.Combine(folder, name);
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            }
            catch { return null; }
        }

        /// <summary>Writes bytes to the given plain folder (overwrites).</summary>
        private static void WriteBytesToFolder(string folder, string name, byte[] data)
        {
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, name);
            File.WriteAllBytes(path, data);
        }

        /// <summary>
        /// Extracts a file from the encrypted archive into memory (returns null if missing).
        /// </summary>
        public static byte[] LoadKeyFromArchiveAsBytes(string name)
        {
            SevenZipBase.SetLibraryPath(sevenZipLibraryPath);

            string archive = SecureEncryptedDataStore.GetString(Key_KeyFile);
            if (string.IsNullOrWhiteSpace(archive) || !File.Exists(archive)) return null;

            char[] pwChars = null;
            string pw = null;
            try
            {
                pwChars = SecureEncryptedDataStore.GetChars(Key_KeyPW);
                pw = new string(pwChars);

                using var extractor = new SevenZipExtractor(archive, pw);
                var entry = extractor.ArchiveFileData.FirstOrDefault(f =>
                    string.Equals(f.FileName, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileName(f.FileName), name, StringComparison.OrdinalIgnoreCase));

                if (entry.FileName == null || entry.IsDirectory) return null;

                using var ms = new MemoryStream();
                extractor.ExtractFile(entry.Index, ms);
                return ms.ToArray();
            }
            catch { return null; }
            finally
            {
                if (pwChars != null) SensitiveDataCleaner.WipeCharArray(pwChars);
                SensitiveDataCleaner.WipeString(ref pw);
            }
        }

        /// <summary>
        /// Rebuilds the encrypted archive to include/update a single file (e.g., keyset.json).
        /// Heavyweight but rare (first time on legacy archives).
        /// </summary>
        public static void SaveKeyToArchive(string name, byte[] bytes)
        {
            SevenZipBase.SetLibraryPath(sevenZipLibraryPath);

            string archive = SecureEncryptedDataStore.GetString(Key_KeyFile);
            if (string.IsNullOrWhiteSpace(archive))
                throw new InvalidOperationException("Key file path is not set.");

            // Extract current archive contents (if any) to temp
            string tempDir = Path.Combine(Path.GetTempPath(), "MWPV_repack_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            char[] pwChars = null;
            string pw = null;
            try
            {
                pwChars = SecureEncryptedDataStore.GetChars(Key_KeyPW);
                pw = new string(pwChars);

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

                // Write/overwrite the target file in temp
                File.WriteAllBytes(Path.Combine(tempDir, name), bytes);

                // Rebuild archive from temp
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
                // Best-effort cleanup
                try { SensitiveDataCleaner.SecureDeleteDirectory(tempDir, overwritePasses: 1, shredNames: true, finalZeroPass: false, removeDirectories: true); } catch { /* ignore */ }
                if (pwChars != null) SensitiveDataCleaner.WipeCharArray(pwChars);
                SensitiveDataCleaner.WipeString(ref pw);
            }
        }
    }
}
