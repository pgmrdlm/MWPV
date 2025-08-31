// Utilities/Security/ServiceSetUp.cs
// First-run provisioning and secure loading utilities (JSON-only archive).

using SevenZip;                       // Squid-Box.SevenZipSharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using Utilities.Helpers;              // ErrorHandler, DatabaseHelper
using Security.Utility.Storage;       // SecureEncryptedDataStore
using Security.Utility.Wiping;        // SensitiveDataCleaner
using Security.Utility.Crypto;        // KeysetJsonV2, KeysetJsonBuilder

namespace Utilities.Security
{
    /// <summary>
    /// First-run provisioning and secure loading utilities.
    /// JSON-only: the encrypted archive contains a single file, "keyset.json",
    /// which holds ALL secrets and ALL SQL text.
    /// </summary>
    internal class ServiceSetUp
    {
        #region Constants / Keys

        private const string Key_KeyPW = "KeyPW";       // char[] (archive password)
        private const string Key_KeyFile = "KeyFile";   // string (archive path)

        private static string LocalRoot =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MWPV");

        private static string SqlFolder => Path.Combine(LocalRoot, "sql");
        private static string DbPath => Path.Combine(LocalRoot, "MWPV.db");

        private const string KeysetJsonName = "keyset.json";

        #endregion

        #region Database setup

        /// <summary>
        /// Ensures local root + SQL staging folder exist, initializes DB from
        /// %LOCALAPPDATA%/MWPV/sql/MWPV_DB_Create.sql, and caches the passwordless connection string.
        /// </summary>
        /// <returns>Local root path on success; "error" on failure.</returns>
        public string SetUpDataBase()
        {
            try
            {
                Directory.CreateDirectory(LocalRoot);
                Directory.CreateDirectory(SqlFolder); // created here; erased after SetUpKeyFile()
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

                // Optional convenience for callers; harmless to keep.
                SecureEncryptedDataStore.SetString("DB_String", $"Data Source={DbPath}");
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

        #region Archive build (JSON-only -> encrypted 7z)

        /// <summary>
        /// Builds (or rebuilds) the encrypted key archive as a single "keyset.json" entry.
        /// keyset.json includes: dbPassword (base64 UTF8), two 32-byte app keys, and ALL *.sql text.
        /// </summary>
        public string SetUpKeyFile()
        {
            string archivePath = null!;
            char[]? keyPw = null;
            char[]? dbPw = null;
            byte[]? logPayloadKey = null;
            byte[]? userSecretsKey = null;

            try
            {
                archivePath = SecureEncryptedDataStore.GetString(Key_KeyFile);
                if (string.IsNullOrWhiteSpace(archivePath))
                    return "Missing KeyFile path.";

                keyPw = SecureEncryptedDataStore.GetChars(Key_KeyPW);
                if (keyPw == null || keyPw.Length == 0)
                    return "Missing key archive password.";

                dbPw = SecureEncryptedDataStore.GetChars(DatabaseHelper.DbPasswordKey);
                if (dbPw == null || dbPw.Length == 0)
                    return "Database password not generated.";

                if (!Directory.Exists(SqlFolder))
                    Directory.CreateDirectory(SqlFolder);

                // Load all SQL files currently in staging
                var sqlMap = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var file in Directory.EnumerateFiles(SqlFolder, "*.sql", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileName(file);
                    var text = File.ReadAllText(file, Encoding.UTF8);
                    sqlMap[name] = text;
                }

                // Generate two 32-byte keys for your app (adjust as needed)
                logPayloadKey = RandomNumberGenerator.GetBytes(32);
                userSecretsKey = RandomNumberGenerator.GetBytes(32);

                // Build JSON
                var json = KeysetJsonBuilder.BuildV2(
                    dbPassword: dbPw,
                    logPayloadKey: logPayloadKey,
                    userSecretsKey: userSecretsKey,
                    sqlMap: sqlMap,
                    appVersionOverride: null
                );

                // Remove any existing archive first (best-effort secure delete)
                if (File.Exists(archivePath))
                {
                    SensitiveDataCleaner.SecureFileDelete(
                        archivePath,
                        overwritePasses: 1,
                        shredName: true,
                        finalZeroPass: true);
                }

                // Write keyset.json to a temp file, then pack as encrypted 7z
                var tempDir = Path.Combine(Path.GetTempPath(), "mwpv_keyset_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                var keysetPath = Path.Combine(tempDir, KeysetJsonName);
                File.WriteAllText(keysetPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                var pwString = new string(keyPw);
                try
                {
                    var comp = new SevenZipCompressor
                    {
                        ArchiveFormat = OutArchiveFormat.SevenZip,
                        CompressionLevel = CompressionLevel.Normal,
                        CompressionMethod = CompressionMethod.Lzma2,
                        EncryptHeaders = true,
                        ZipEncryptionMethod = ZipEncryptionMethod.Aes256,
                        PreserveDirectoryRoot = false
                    };

                    // Single-entry archive; internal name is "keyset.json"
                    comp.CompressFilesEncrypted(archivePath, pwString, keysetPath);
                }
                finally
                {
                    SensitiveDataCleaner.WipeString(ref pwString);
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
                }

                // Scrub staging folder now that everything lives in the archive
                SecurelyScrubSqlStagingFolder();

                return $"Encrypted archive created at: {archivePath}";
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error creating archive.");
                try { SecurelyScrubSqlStagingFolder(); } catch { /* best-effort */ }
                return "Error creating archive: " + ex.Message;
            }
            finally
            {
                if (dbPw != null) SensitiveDataCleaner.WipeCharArray(dbPw);
                if (keyPw != null) SensitiveDataCleaner.WipeCharArray(keyPw);
                if (logPayloadKey != null) Array.Clear(logPayloadKey, 0, logPayloadKey.Length);
                if (userSecretsKey != null) Array.Clear(userSecretsKey, 0, userSecretsKey.Length);
                archivePath = null!;
            }
        }

        private static void SecurelyScrubSqlStagingFolder()
        {
            if (!Directory.Exists(SqlFolder)) return;

            // Wipe contents…
            SensitiveDataCleaner.SecureDeleteAllFiles(SqlFolder, overwritePasses: 3);

            // …then remove the directory (name shredding).
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

        #region Archive read (JSON-only)

        /// <summary>
        /// Open the encrypted 7z from KeyFile using KeyPW, parse keyset.json,
        /// and load the DB password + all SQL entries into SecureEncryptedDataStore.
        /// </summary>
        public static void EnsureKeySetFromArchive()
        {
            char[]? keyPw = null;
            try
            {
                var archivePath = SecureEncryptedDataStore.GetString(Key_KeyFile);
                if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
                    throw new FileNotFoundException("Key archive not found.", archivePath ?? "(null)");

                keyPw = SecureEncryptedDataStore.GetChars(Key_KeyPW);
                var pwString = new string(keyPw ?? Array.Empty<char>());

                using var extractor = new SevenZipExtractor(archivePath, pwString);
                SensitiveDataCleaner.WipeString(ref pwString);

                // Find "keyset.json" (match either full name or base name)
                var entry = extractor.ArchiveFileData.FirstOrDefault(f =>
                    string.Equals(f.FileName, KeysetJsonName, StringComparison.Ordinal) ||
                    string.Equals(Path.GetFileName(f.FileName), KeysetJsonName, StringComparison.Ordinal));

                if (entry.FileName == null || entry.IsDirectory)
                    throw new InvalidOperationException("keyset.json missing from archive.");

                using var ms = new MemoryStream();
                extractor.ExtractFile(entry.Index, ms);
                ms.Position = 0;

                string json;
                using (var sr = new StreamReader(ms, Encoding.UTF8, true, 1024, leaveOpen: true))
                    json = sr.ReadToEnd();

                var ks = KeysetJsonV2.Deserialize(json); // validates version & presence

                // ---- Secrets ----
                var dbPwChars = KeysetJsonV2.DecodeDbPasswordToChars(ks.secrets.dbPassword);
                SecureEncryptedDataStore.SetAndWipe(DatabaseHelper.DbPasswordKey, dbPwChars);

                // ---- SQL ----
                foreach (var kvp in ks.sql)
                {
                    if (!string.IsNullOrWhiteSpace(kvp.Key))
                        SecureEncryptedDataStore.SetString(kvp.Key, kvp.Value ?? string.Empty);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[ServiceSetUp] EnsureKeySetFromArchive error: " + ex.Message);
            }
            finally
            {
                if (keyPw != null) SensitiveDataCleaner.WipeCharArray(keyPw);
            }
        }

        /// <summary>
        /// JSON-only archives don't expose individual files. We rely on EnsureKeySetFromArchive()
        /// having already populated SEDS. For compatibility with existing call sites, we just
        /// report whether the requested key is present in SEDS.
        /// </summary>
        /// <returns>"worked" if key exists in SEDS, otherwise "not_found".</returns>
        public static string LoadSqlFromEncryptedArchive(string fileNameInArchive)
        {
            return SecureEncryptedDataStore.HasKey(fileNameInArchive) ? "worked" : "not_found";
        }

        #endregion
    }
}
