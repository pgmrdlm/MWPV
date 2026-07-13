// File: Utilities/Security/ServiceSetUp.cs
// First-run provisioning and secure loading utilities (JSON-only archive).
//
// Responsibilities:
//  1) Create LocalRoot + DB file (schema is expected to already be in SEDS OR in SqlFolder staging).
//  2) Build encrypted key archive containing ONE file: keyset.json (secrets + sql map).
//  3) Load keyset.json from archive -> seed SEDS (DB password + SQL text + optional keys).
//
// Notes:
//  - NO integrity table.
//  - SQL staging folder is optional and is scrubbed after archive build.
//  - This class should NOT contain “random helpers” that return string "worked"/"not_found".
//    Consumers should read from SEDS via SecureSql.Require(...) or equivalent.

using Security.Utility.Crypto;           // KeysetJsonV2, KeysetJsonBuilder
using Security.Utility.Storage;          // SecureEncryptedDataStore (SEDS)
using Security.Utility.Wiping;           // SensitiveDataCleaner
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Utilities.Helpers;                 // ErrorHandler, DatabaseHelper
using KeyFileLogic;                      // Draft SQLite key-file storage
using MWPV.SqlCatalog;

namespace Utilities.Security
{
    internal sealed class ServiceSetUp
    {
        #region SEDS Keys / Constants

        // These are the two values the UI must put into SEDS before calling build/load.
        private const string SedsKey_KeyPW = "KeyPW";     // char[] (archive password)
        private const string SedsKey_KeyFile = "KeyFile"; // string (archive path)

        // AES keys that live in keyset.json and MUST be loaded into SEDS at startup.
        private const string SedsKey_LogPayloadKey = "LogPayloadKey";   // byte[32]
        private const string SedsKey_UserSecretsKey = "UserSecretsKey"; // byte[32]

        // We keep these paths centralized here so callers don't duplicate them.
        private static string LocalRoot =>
            Path.Combine(AppPaths.LocalAppDataRoot(), "MWPV");


        private static string SqlFolder => Path.Combine(LocalRoot, "sql");
        private static string DbPath => Path.Combine(LocalRoot, "MWPV.db");

        private const string KeysetJsonName = "keyset.json";
        private const long KeysetPayloadId = 1;

        // If we must read schema from disk, this is the filename we expect in staging.
        private const string SchemaFileName = "MWPV_DB_Create.sql";

        // One-time guard so we don't extract keyset.json for every SQL artifact request.
        private static int _keysetLoaded; // 0 = no, 1 = yes
        private static readonly object _keysetLoadLock = new();
        private static IReadOnlyDictionary<string, string> _loadedSqlPayload = new Dictionary<string, string>(StringComparer.Ordinal);

        #endregion

        #region Public API

        /// <summary>
        /// Ensures LocalRoot exists, ensures DB file exists, and applies schema.
        /// Source for schema:
        ///   1) SEDS entry (preferred): SchemaFileName
        ///   2) SqlFolder staging file (fallback)
        /// </summary>
        public string SetUpDataBase(VerifiedNewInstallPackage package)
        {
            string? schemaSql = null;

            try
            {
                EnsureLocalFolders();

                schemaSql = package?.DatabaseCreationScript.SqlText;
                if (string.IsNullOrWhiteSpace(schemaSql))
                {
                    throw new InvalidOperationException(
                        $"Schema SQL missing. Expected SEDS['{SchemaFileName}'] or {Path.Combine(SqlFolder, SchemaFileName)}");
                }

                using (var cn = DatabaseHelper.OpenConnection())
                using (var cmd = cn.CreateCommand())
                {
                    cmd.CommandText = schemaSql;
                    cmd.ExecuteNonQuery();
                }

                // If your app uses this, fine. If not, remove it (but don’t duplicate it elsewhere).
                SecureEncryptedDataStore.SetString("DB_String", $"Data Source={DbPath}");

                return LocalRoot;
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error creating database file.");
                return "error";
            }
            finally
            {
                if (schemaSql != null)
                    SensitiveDataCleaner.WipeString(ref schemaSql);
            }
        }

        public string SetUpKeyFile(VerifiedNewInstallPackage package)
        {
            // Production routing draft:
            // SetUpKeyFile() -> SetUpKeyFile_Sqlite()
            return SetUpKeyFile_Sqlite(package);
        }

        /// <summary>
        /// Draft SQLite key-file writer. Keeps keyset.json unchanged and stores it as one encrypted
        /// SQLite payload row instead of wrapping it in a 7z archive.
        /// </summary>
        public string SetUpKeyFile_Sqlite(VerifiedNewInstallPackage package)
        {
            string? keyFilePath = null;
            char[]? keyPw = null;
            char[]? dbPw = null;
            byte[]? logPayloadKey = null;
            byte[]? userSecretsKey = null;
            byte[]? keysetBytes = null;
            string? json = null;

            try
            {
                EnsureLocalFolders();

                keyFilePath = GetRequiredArchivePathFromSeds();
                keyPw = GetRequiredKeyPasswordFromSeds();
                dbPw = GetRequiredDbPasswordFromSeds();

                if (package == null) throw new ArgumentNullException(nameof(package));
                var sqlMap = package.KeyFilePayloadScripts.ToDictionary(x => x.CatalogEntry.FileName, x => x.SqlText, StringComparer.OrdinalIgnoreCase);
                SeedSedsWithSql(sqlMap);

                logPayloadKey = RandomNumberGenerator.GetBytes(32);
                userSecretsKey = RandomNumberGenerator.GetBytes(32);

                json = KeysetJsonBuilder.BuildV2(
                    dbPassword: dbPw,
                    logPayloadKey: logPayloadKey,
                    userSecretsKey: userSecretsKey,
                    sqlMap: sqlMap,
                    appVersionOverride: null
                );

                keysetBytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(json);

                var (directory, fileName) = SplitKeyFilePath(keyFilePath);

                KeyFileStore.SavePayload(
                    directory,
                    fileName,
                    keyPw,
                    KeysetPayloadId,
                    keysetBytes);

                // FUTURE DRAFT NOTE:
                // Add post-save validation here: reopen the SQLite key file, read PayloadId=1,
                // and validate that the bytes deserialize as the expected keyset.json.

                Volatile.Write(ref _keysetLoaded, 0);
                SqlStagingCleanupService.SecurelyScrubDefaultStagingFolder();

                return $"Encrypted SQLite key file created at: {keyFilePath}";
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error creating SQLite key file.");
                try { SqlStagingCleanupService.SecurelyScrubDefaultStagingFolder(); } catch { /* best-effort */ }
                return "Error creating SQLite key file: " + ex.Message;
            }
            finally
            {
                if (json != null) SensitiveDataCleaner.WipeString(ref json);
                if (keysetBytes != null) Array.Clear(keysetBytes, 0, keysetBytes.Length);
                if (dbPw != null) SensitiveDataCleaner.WipeCharArray(dbPw);
                if (keyPw != null) SensitiveDataCleaner.WipeCharArray(keyPw);
                if (logPayloadKey != null) Array.Clear(logPayloadKey, 0, logPayloadKey.Length);
                if (userSecretsKey != null) Array.Clear(userSecretsKey, 0, userSecretsKey.Length);
                keyFilePath = null;
            }
        }

        /// <summary>
        /// Loads keyset.json from the encrypted archive (path/password in SEDS) and seeds SEDS with:
        ///   - db password (DatabaseHelper.DbPasswordKey) as char[] via SetAndWipe
        ///   - sql map (filename -> sql text) via SetString
        ///   - AES keys (LogPayloadKey/UserSecretsKey) as byte[] via Set + wipe buffers
        /// This is the normal "startup load" path.
        /// </summary>
        public static void EnsureKeySetFromArchive()
        {
            // Production routing draft:
            // EnsureKeySetFromArchive() -> EnsureKeySetLoadedFromKeyFile()
            // Name remains temporarily for existing callers.
            EnsureKeySetLoadedFromKeyFile();
        }

        /// <summary>
        /// Draft SQLite key-file loader. Reads keyset.json bytes from payload row 1 and seeds SEDS
        /// exactly like the legacy archive loader.
        /// </summary>
        public static void EnsureKeySetLoadedFromKeyFile()
        {
            char[]? keyPw = null;
            string? json = null;
            byte[]? keysetBytes = null;
            byte[]? logKey = null;
            byte[]? userKey = null;

            try
            {
                string keyFilePath = GetRequiredArchivePathFromSedsStatic();
                if (!File.Exists(keyFilePath))
                    throw new FileNotFoundException("SQLite key file not found.", keyFilePath);

                keyPw = GetRequiredKeyPasswordFromSedsStatic();
                var (directory, fileName) = SplitKeyFilePath(keyFilePath);

                keysetBytes = KeyFileStore.ReadPayloadBytes(
                    directory,
                    fileName,
                    keyPw,
                    KeysetPayloadId);

                json = Encoding.UTF8.GetString(keysetBytes);
                SeedSedsFromKeysetJson(json, ref logKey, ref userKey);

                Volatile.Write(ref _keysetLoaded, 1);
            }
            catch (Exception ex)
            {
                _ = FatalErrorPopupHelper.ShowFatalAsync(
                    "MWPV could not load the required encryption keyset and must close.",
                    ex,
                    "The encrypted SQLite startup key file could not be opened or parsed.");
            }
            finally
            {
                if (json != null) SensitiveDataCleaner.WipeString(ref json);
                if (keysetBytes != null) Array.Clear(keysetBytes, 0, keysetBytes.Length);
                if (logKey != null) Array.Clear(logKey, 0, logKey.Length);
                if (userKey != null) Array.Clear(userKey, 0, userKey.Length);
                if (keyPw != null) SensitiveDataCleaner.WipeCharArray(keyPw);
            }
        }

        /// <summary>
        /// Compatibility shim for existing callers (SqlCategory.LoadAll()).
        /// Ensures keyset.json has been loaded into SEDS, then verifies the requested SQL is present.
        /// </summary>
        public static void LoadSqlFromEncryptedArchive(string fileNameInArchive)
        {
            // Production routing draft:
            // LoadSqlFromEncryptedArchive(...) -> LoadArtifactFromKeyFile(...)
            // Name remains temporarily for existing callers.
            LoadArtifactFromKeyFile(fileNameInArchive);
        }

        /// <summary>
        /// Storage-neutral draft loader. Ensures the keyset has been loaded from the SQLite key file,
        /// then verifies that the requested SQL artifact is present in SEDS.
        /// </summary>
        public static void LoadArtifactFromKeyFile(string fileNameInArchive)
        {
            if (string.IsNullOrWhiteSpace(fileNameInArchive))
                throw new ArgumentException("SQL filename cannot be null/empty.", nameof(fileNameInArchive));

            if (SecureEncryptedDataStore.HasKey(fileNameInArchive))
                return;

            EnsureKeysetLoadedOnce();

            if (!SecureEncryptedDataStore.HasKey(fileNameInArchive))
            {
                throw new FileNotFoundException(
                    $"SQL artifact '{fileNameInArchive}' was not found in SEDS after loading keyset.json. " +
                    $"Check the SQLite key file payload and the required SQL catalog names.",
                    fileNameInArchive);
            }
        }

        public static IReadOnlyList<SqlFileInput> GetVerifiedPayloadCandidates()
        {
            return _loadedSqlPayload.Select(pair => new SqlFileInput(
                pair.Key,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(pair.Value ?? string.Empty),
                "decrypted key-file payload")).ToArray();
        }

        /// <summary>
        /// Minimal loader used by validators. Reads raw bytes of keyset.json from the encrypted archive.
        /// Returns Array.Empty&lt;byte&gt; on failure.
        /// </summary>
        public byte[] LoadKeysetJsonBytes()
        {
            // Production routing draft:
            // LoadKeysetJsonBytes() -> LoadKeysetJsonBytesFromKeyFile()
            // Existing-login verification in AppEntryWindow still needs a later SQLite validator swap.
            return LoadKeysetJsonBytesFromKeyFile();
        }

        /// <summary>
        /// Draft validator helper. Reads raw keyset.json bytes from the encrypted SQLite key file.
        /// </summary>
        public byte[] LoadKeysetJsonBytesFromKeyFile()
        {
            char[]? keyPw = null;

            try
            {
                // FUTURE DRAFT NOTE:
                // AppEntryWindow existing-login validation still needs a SQLite equivalent for
                // KeyArchiveVerifier.VerifyPasswordAndSentinels(...): open/validate SQLite schema,
                // read PayloadId=1, and validate keyset.json.
                string keyFilePath = GetRequiredArchivePathFromSeds();
                if (!File.Exists(keyFilePath))
                    return Array.Empty<byte>();

                keyPw = GetRequiredKeyPasswordFromSeds();
                var (directory, fileName) = SplitKeyFilePath(keyFilePath);

                return KeyFileStore.ReadPayloadBytes(
                    directory,
                    fileName,
                    keyPw,
                    KeysetPayloadId);
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

        #endregion

        #region Internals - keyset loaded once

        private static void EnsureKeysetLoadedOnce()
        {
            if (Volatile.Read(ref _keysetLoaded) == 1)
                return;

            lock (_keysetLoadLock)
            {
                if (_keysetLoaded == 1)
                    return;

                EnsureKeySetLoadedFromKeyFile();
                // EnsureKeySetLoadedFromKeyFile sets _keysetLoaded=1 on success.
            }
        }

        #endregion

        #region Internals - folders / schema

        private static void EnsureLocalFolders()
        {
            try
            {
                Directory.CreateDirectory(LocalRoot);
                Directory.CreateDirectory(SqlFolder);
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Unable to create MWPV local folders.");
                throw;
            }
        }

        #endregion

        #region Internals - archive build helpers

        private static string GetRequiredArchivePathFromSeds()
        {
            var archivePath = SecureEncryptedDataStore.GetString(SedsKey_KeyFile);
            if (string.IsNullOrWhiteSpace(archivePath))
                throw new InvalidOperationException("Missing KeyFile path in SEDS.");
            return archivePath;
        }

        private static char[] GetRequiredKeyPasswordFromSeds()
        {
            var keyPw = SecureEncryptedDataStore.GetChars(SedsKey_KeyPW);
            if (keyPw == null || keyPw.Length == 0)
                throw new InvalidOperationException("Missing key archive password in SEDS.");
            return keyPw;
        }

        private static char[] GetRequiredDbPasswordFromSeds()
        {
            var dbPw = SecureEncryptedDataStore.GetChars(DatabaseHelper.DbPasswordKey);
            if (dbPw == null || dbPw.Length == 0)
                throw new InvalidOperationException("Database password not generated.");
            return dbPw;
        }

        private static void SeedSedsWithSql(Dictionary<string, string> sqlMap)
        {
            foreach (var kv in sqlMap)
            {
                if (string.IsNullOrWhiteSpace(kv.Key))
                    continue;

                SecureEncryptedDataStore.SetString(kv.Key, kv.Value ?? string.Empty);
            }
        }

        private static void SeedSedsFromKeysetJson(string json, ref byte[]? logKey, ref byte[]? userKey)
        {
            var ks = KeysetJsonV2.Deserialize(json);

            var dbPwChars = KeysetJsonV2.DecodeDbPasswordToChars(ks.secrets.dbPassword);
            SecureEncryptedDataStore.SetAndWipe(DatabaseHelper.DbPasswordKey, dbPwChars);

            var payload = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kvp in ks.sql)
            {
                if (!string.IsNullOrWhiteSpace(kvp.Key))
                {
                    SecureEncryptedDataStore.SetString(kvp.Key, kvp.Value ?? string.Empty);
                    payload[kvp.Key] = kvp.Value ?? string.Empty;
                }
            }
            _loadedSqlPayload = payload;

            if (!string.IsNullOrWhiteSpace(ks.secrets.logPayloadKey))
            {
                logKey = Convert.FromBase64String(ks.secrets.logPayloadKey);
                SecureEncryptedDataStore.Set(SedsKey_LogPayloadKey, logKey);
            }

            if (!string.IsNullOrWhiteSpace(ks.secrets.userSecretsKey))
            {
                userKey = Convert.FromBase64String(ks.secrets.userSecretsKey);
                SecureEncryptedDataStore.Set(SedsKey_UserSecretsKey, userKey);
            }
        }

        private static (string Directory, string FileName) SplitKeyFilePath(string keyFilePath)
        {
            // FUTURE DRAFT NOTE:
            // UI save/open filters still describe 7z archives. The SQLite key-file path will need
            // updated extension/default-name/filter language after the draft path is reviewed.
            var directory = Path.GetDirectoryName(keyFilePath);
            var fileName = Path.GetFileName(keyFilePath);

            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
                throw new InvalidOperationException("KeyFile path must include both directory and file name.");

            return (directory, fileName);
        }

        #endregion

        #region Internals - static helpers (for static EnsureKeySetFromArchive)

        private static string GetRequiredArchivePathFromSedsStatic()
        {
            var archivePath = SecureEncryptedDataStore.GetString(SedsKey_KeyFile);
            if (string.IsNullOrWhiteSpace(archivePath))
                throw new InvalidOperationException("Missing KeyFile path in SEDS.");
            return archivePath;
        }

        private static char[] GetRequiredKeyPasswordFromSedsStatic()
        {
            var keyPw = SecureEncryptedDataStore.GetChars(SedsKey_KeyPW);
            if (keyPw == null || keyPw.Length == 0)
                throw new InvalidOperationException("Missing key archive password in SEDS.");
            return keyPw;
        }

        #endregion
    }
}
