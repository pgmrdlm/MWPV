using Microsoft.Data.Sqlite;
using SevenZip;
using System;
using System.IO;
using System.Linq;
using System.Text;
using Utilities.Helpers;
using Utilities.Security;

namespace Utilities.Security
{
    internal class ServiceSetUp
    {
        private const string Key_KeyPW = "KeyPW";
        private const string Key_KeyFile = "KeyFile";
        private const string Key_DbPwPath = "DB_Password_Path";
        private const string Key_DbConnNoPw = "DB_String";

        // NOTE: ensure this path is valid on the target machine
        private static readonly string sevenZipLibraryPath = @"C:\Users\pgmrd\My Drive\MWPV\MWPV\7z.dll";

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

                // Temp location for the DB password under the canonical filename
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

        public string SetUpKeyFile()
        {
            string strDirectoryToCompress = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MWPV", "sql"
            );

            string strKeyFilePath = SecureEncryptedDataStore.GetString(Key_KeyFile);

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

                // Copy the temp DB password into the sql folder under the canonical filename before zipping
                string strtmppwfile = SecureEncryptedDataStore.GetString(Key_DbPwPath);
                if (!string.IsNullOrWhiteSpace(strtmppwfile) && File.Exists(strtmppwfile))
                {
                    File.Copy(strtmppwfile, Path.Combine(strDirectoryToCompress, DatabaseHelper.DbPasswordKey), true);
                    SensitiveDataCleaner.SecureFileDelete(strtmppwfile);
                }
        
                if (!string.IsNullOrWhiteSpace(strKeyFilePath) && File.Exists(strKeyFilePath))
                {
                    // Best effort secure delete (1 pass, shred name, final zero pass)
                    SensitiveDataCleaner.SecureFileDelete(
                        strKeyFilePath,
                        overwritePasses: 1,
                        shredName: true,
                        finalZeroPass: true
                    );
                }

                var compressor = new SevenZipCompressor
                {
                    ArchiveFormat = OutArchiveFormat.SevenZip,
                    CompressionLevel = CompressionLevel.Ultra,
                    CompressionMethod = CompressionMethod.Lzma2,
                    EncryptHeaders = true,
                    ZipEncryptionMethod = ZipEncryptionMethod.Aes256,
                    PreserveDirectoryRoot = true
                };

                compressor.CompressFilesEncrypted(strKeyFilePath, strKeyFilePW, files);

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
        /// Returns false for both wrong password and invalid/corrupt key files.
        /// </summary>
        public static bool VerifyKeyFilePW(string archivePath, string password)
        {
            try
            {
                SevenZipBase.SetLibraryPath(sevenZipLibraryPath);

                using (var extractor = new SevenZipExtractor(archivePath, password))
                {
                    // Force the extractor to actually try reading
                    _ = extractor.ArchiveFileData.FirstOrDefault();
                    return true;
                }
            }
            catch (SevenZipException)
            {
                // Wrong password or corrupt archive

                return false;
            }
            catch (ArgumentException)
            {
                // Invalid file type (e.g., not a 7z archive)
                return false;
            }
            catch
            {
                // Any other fatal error
                return false;
            }
            finally
            {
                SensitiveDataCleaner.WipeSensitiveStrings(ref password, ref archivePath);
            }
        }

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
                // Wrong password or invalid file → handled same as UI
                EarlyLoginFailures.Record(EarlyFailType.KeyfileMissingOrCorrupt, ex.Message);
                return "error";
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Unexpected error loading from archive.");
                return "error";
            }
        }
    }
}
