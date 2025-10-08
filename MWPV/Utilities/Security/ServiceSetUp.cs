// File: Utilities/Security/ServiceSetUp.cs
// First-run provisioning and secure loading utilities (JSON-only archive).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Security.Utility.Archives;        // SevenZipCore
using Security.Utility.Storage;         // SecureEncryptedDataStore
using Security.Utility.Wiping;          // SensitiveDataCleaner
using Security.Utility.Crypto;          // KeysetJsonV2, KeysetJsonBuilder
using Utilities.Helpers;                // ErrorHandler, DatabaseHelper
using Utilities.Security;               // KeyArchiveIntegrityService

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

        private const string Key_KeyPW = "KeyPW";     // char[] (archive password)
        private const string Key_KeyFile = "KeyFile";   // string (archive path)

        private static string LocalRoot =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MWPV");

        private static string SqlFolder => Path.Combine(LocalRoot, "sql");
        private static string DbPath => Path.Combine(LocalRoot, "MWPV.db");

        private const string KeysetJsonName = "keyset.json";

        #endregion

        #region Database setup

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

        public string SetUpKeyFile()
        {
            string? archivePath = null;
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

                // Gather *.sql from staging folder (these get embedded into keyset.json)
                var sqlMap = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var file in Directory.EnumerateFiles(SqlFolder, "*.sql", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileName(file);
                    var text = File.ReadAllText(file, Encoding.UTF8);
                    sqlMap[name] = text;
                }

                // NEW: Seed SEDS with SQL now so SecureSql.Require(...) works on first run
                foreach (var kv in sqlMap)
                {
                    SecureEncryptedDataStore.SetString(kv.Key, kv.Value ?? string.Empty);
                }

                // Generate additional keys
                logPayloadKey = RandomNumberGenerator.GetBytes(32);
                userSecretsKey = RandomNumberGenerator.GetBytes(32);

                // Build keyset.json from secrets + SQL catalog
                var json = KeysetJsonBuilder.BuildV2(
                    dbPassword: dbPw,
                    logPayloadKey: logPayloadKey,
                    userSecretsKey: userSecretsKey,
                    sqlMap: sqlMap,
                    appVersionOverride: null
                );

                // Replace existing archive (secure delete first)
                if (File.Exists(archivePath))
                {
                    SensitiveDataCleaner.SecureFileDelete(
                        archivePath,
                        overwritePasses: 1,
                        shredName: true,
                        finalZeroPass: true);
                }

                // Write temporary keyset.json and compress into encrypted archive
                var tempDir = Path.Combine(Path.GetTempPath(), "mwpv_keyset_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                var keysetPath = Path.Combine(tempDir, KeysetJsonName);
                File.WriteAllText(keysetPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                var pwString = new string(keyPw);
                try
                {
                    var comp = SevenZipCore.CreateCompressor();

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

                // Record size + sha of the archive we just created.
                // (DDL creates KeyArchiveIntegrity; required SQL is already in SEDS.)
                try
                {
                    KeyArchiveIntegrityService.UpsertFromArchivePath(archivePath);
                }
                catch (Exception upsertEx)
                {
                    ErrorHandler.Abend(upsertEx, "Failed to record key archive integrity.");
                    return "error";
                }

                // Clean up any staged SQL now that it is sealed into the archive
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
                archivePath = null;
            }
        }

        private static void SecurelyScrubSqlStagingFolder()
        {
            if (!Directory.Exists(SqlFolder)) return;

            SensitiveDataCleaner.SecureDeleteAllFiles(SqlFolder, overwritePasses: 3);

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

                using var extractor = SevenZipCore.CreateExtractor(archivePath, pwString);
                SensitiveDataCleaner.WipeString(ref pwString);

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

                var ks = KeysetJsonV2.Deserialize(json);

                var dbPwChars = KeysetJsonV2.DecodeDbPasswordToChars(ks.secrets.dbPassword);
                SecureEncryptedDataStore.SetAndWipe(DatabaseHelper.DbPasswordKey, dbPwChars);

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
        /// Minimal loader used by KeyProvisioner.ValidateKeysetJson.
        /// Reads raw bytes of "keyset.json" from the encrypted archive using SEDS path/password.
        /// Returns Array.Empty&lt;byte&gt; on any failure.
        /// </summary>
        public byte[] LoadKeysetJsonBytes()
        {
            char[]? keyPw = null;
            try
            {
                var archivePath = SecureEncryptedDataStore.GetString(Key_KeyFile);
                if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
                    return Array.Empty<byte>();

                keyPw = SecureEncryptedDataStore.GetChars(Key_KeyPW);
                var pwString = new string(keyPw ?? Array.Empty<char>());
                try
                {
                    using var extractor = SevenZipCore.CreateExtractor(archivePath, pwString);

                    var entry = extractor.ArchiveFileData.FirstOrDefault(f =>
                        !f.IsDirectory && (
                            string.Equals(f.FileName, KeysetJsonName, StringComparison.Ordinal) ||
                            string.Equals(Path.GetFileName(f.FileName), KeysetJsonName, StringComparison.Ordinal)));

                    if (entry.FileName == null || entry.IsDirectory)
                        return Array.Empty<byte>();

                    using var ms = new MemoryStream();
                    extractor.ExtractFile(entry.Index, ms);
                    return ms.ToArray();
                }
                finally
                {
                    SensitiveDataCleaner.WipeString(ref pwString);
                }
            }
            catch
            {
                return Array.Empty<byte>();
            }
            finally
            {
                if (keyPw != null) SensitiveDataCleaner.WipeCharArray(keyPw);
            }
        }

        public static string LoadSqlFromEncryptedArchive(string fileNameInArchive)
        {
            return SecureEncryptedDataStore.HasKey(fileNameInArchive) ? "worked" : "not_found";
        }

        #endregion
    }
}
