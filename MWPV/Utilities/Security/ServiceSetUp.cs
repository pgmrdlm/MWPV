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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Utilities.Helpers;                 // ErrorHandler, DatabaseHelper
using Security.Utility.Archives;         // SevenZipCore
using Security.Utility.Storage;          // SecureEncryptedDataStore (SEDS)
using Security.Utility.Wiping;           // SensitiveDataCleaner
using Security.Utility.Crypto;           // KeysetJsonV2, KeysetJsonBuilder

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

        // If we must read schema from disk, this is the filename we expect in staging.
        private const string SchemaFileName = "MWPV_DB_Create.sql";

        // One-time guard so we don't extract keyset.json for every SQL artifact request.
        private static int _keysetLoaded; // 0 = no, 1 = yes
        private static readonly object _keysetLoadLock = new();

        #endregion

        #region Public API

        /// <summary>
        /// Ensures LocalRoot exists, ensures DB file exists, and applies schema.
        /// Source for schema:
        ///   1) SEDS entry (preferred): SchemaFileName
        ///   2) SqlFolder staging file (fallback)
        /// </summary>
        public string SetUpDataBase()
        {
            string? schemaSql = null;

            try
            {
                EnsureLocalFolders();

                schemaSql = TryGetSchemaSqlFromSedsOrDisk();
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

        /// <summary>
        /// Builds an encrypted 7z archive at SEDS['KeyFile'] containing one entry: keyset.json.
        /// keyset.json contains:
        ///   - secrets (dbPassword + additional keys)
        ///   - sql map (filename -> sql text)
        /// Then scrubs SqlFolder staging.
        /// </summary>
        public string SetUpKeyFile()
        {
            string? archivePath = null;
            char[]? keyPw = null;
            char[]? dbPw = null;
            byte[]? logPayloadKey = null;
            byte[]? userSecretsKey = null;

            try
            {
                EnsureLocalFolders();

                archivePath = GetRequiredArchivePathFromSeds();
                keyPw = GetRequiredKeyPasswordFromSeds();
                dbPw = GetRequiredDbPasswordFromSeds();

                // Gather SQL to embed into keyset.json.
                // Prefer staging folder (current behavior), but allow fallback to SEDS if staging is empty.
                var sqlMap = LoadSqlMapFromStagingOrSeds();

                // Seed SEDS with SQL now so SecureSql.Require(...) works immediately on first run
                // (and also so DB setup can read the schema from SEDS if you use that path).
                SeedSedsWithSql(sqlMap);

                // Generate additional keys (stored in keyset.json)
                logPayloadKey = RandomNumberGenerator.GetBytes(32);
                userSecretsKey = RandomNumberGenerator.GetBytes(32);

                // Build keyset.json from secrets + SQL map
                string json = KeysetJsonBuilder.BuildV2(
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

                // Create temp dir -> write keyset.json -> compress encrypted -> scrub temp
                string tempDir = Path.Combine(Path.GetTempPath(), "mwpv_keyset_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);

                string keysetPath = Path.Combine(tempDir, KeysetJsonName);
                File.WriteAllText(keysetPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                string pwString = new string(keyPw);
                try
                {
                    var comp = SevenZipCore.CreateCompressor();
                    comp.CompressFilesEncrypted(archivePath, pwString, keysetPath);
                }
                finally
                {
                    SensitiveDataCleaner.WipeString(ref pwString);
                    TrySecureDeleteTempDir(tempDir);

                    // json contains secrets. wipe it.
                    SensitiveDataCleaner.WipeString(ref json);
                }

                // Mark as not-loaded so next run (or same run) can re-seed from archive if needed.
                Volatile.Write(ref _keysetLoaded, 0);

                // Clean up staged SQL now that it is sealed into the archive
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

        /// <summary>
        /// Loads keyset.json from the encrypted archive (path/password in SEDS) and seeds SEDS with:
        ///   - db password (DatabaseHelper.DbPasswordKey) as char[] via SetAndWipe
        ///   - sql map (filename -> sql text) via SetString
        ///   - AES keys (LogPayloadKey/UserSecretsKey) as byte[] via Set + wipe buffers
        /// This is the normal "startup load" path.
        /// </summary>
        public static void EnsureKeySetFromArchive()
        {
            char[]? keyPw = null;
            string? pwString = null;

            string? json = null;
            byte[]? logKey = null;
            byte[]? userKey = null;

            try
            {
                string archivePath = GetRequiredArchivePathFromSedsStatic();
                if (!File.Exists(archivePath))
                    throw new FileNotFoundException("Key archive not found.", archivePath);

                keyPw = GetRequiredKeyPasswordFromSedsStatic();
                pwString = new string(keyPw);

                using var extractor = SevenZipCore.CreateExtractor(archivePath, pwString);

                var entry = extractor.ArchiveFileData.FirstOrDefault(f =>
                    !f.IsDirectory &&
                    (string.Equals(f.FileName, KeysetJsonName, StringComparison.Ordinal) ||
                     string.Equals(Path.GetFileName(f.FileName), KeysetJsonName, StringComparison.Ordinal)));

                if (string.IsNullOrWhiteSpace(entry.FileName))
                    throw new InvalidOperationException("keyset.json missing from archive.");

                using var ms = new MemoryStream();
                extractor.ExtractFile(entry.Index, ms);
                ms.Position = 0;

                using (var sr = new StreamReader(ms, Encoding.UTF8, true, 1024, leaveOpen: true))
                    json = sr.ReadToEnd();

                var ks = KeysetJsonV2.Deserialize(json);

                // DB password -> SEDS (char[]), wipe source buffers
                var dbPwChars = KeysetJsonV2.DecodeDbPasswordToChars(ks.secrets.dbPassword);
                SecureEncryptedDataStore.SetAndWipe(DatabaseHelper.DbPasswordKey, dbPwChars);

                // SQL -> SEDS
                foreach (var kvp in ks.sql)
                {
                    if (!string.IsNullOrWhiteSpace(kvp.Key))
                        SecureEncryptedDataStore.SetString(kvp.Key, kvp.Value ?? string.Empty);
                }

                // AES keys -> SEDS (these are what your FieldAesCrypto expects)
                // IMPORTANT: store as byte[] and wipe local buffers after Set.
                //
                // KeysetJsonV2 is expected to expose these as base64 strings (or null).
                // If they are missing/null, we simply don't set them here (caller may fail fast later).
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

                // Mark as loaded (one-time)
                Volatile.Write(ref _keysetLoaded, 1);

//#if DEBUG
//                System.Diagnostics.Debug.WriteLine(
//                    "[ServiceSetUp] Keyset loaded into SEDS. " +
//                    $"SQL keys present: {ks.sql?.Count ?? 0}, " +
//                    $"Has LogPayloadKey: {SecureEncryptedDataStore.HasKey(SedsKey_LogPayloadKey)}, " +
//                    $"Has UserSecretsKey: {SecureEncryptedDataStore.HasKey(SedsKey_UserSecretsKey)}");
//#endif
            }
            catch (Exception ex)
            {
//#if DEBUG
//                System.Diagnostics.Debug.WriteLine("[ServiceSetUp] EnsureKeySetFromArchive error: " + ex);
//#endif
                // Caller decides UI/termination behavior.
            }
            finally
            {
                // Wipe sensitive buffers
                if (json != null) SensitiveDataCleaner.WipeString(ref json);

                if (logKey != null) Array.Clear(logKey, 0, logKey.Length);
                if (userKey != null) Array.Clear(userKey, 0, userKey.Length);

                if (pwString != null) SensitiveDataCleaner.WipeString(ref pwString);
                if (keyPw != null) SensitiveDataCleaner.WipeCharArray(keyPw);
            }
        }

        /// <summary>
        /// Compatibility shim for existing callers (SqlCategory.LoadAll()).
        /// Ensures keyset.json has been loaded into SEDS, then verifies the requested SQL is present.
        /// </summary>
        public static void LoadSqlFromEncryptedArchive(string fileNameInArchive)
        {
            if (string.IsNullOrWhiteSpace(fileNameInArchive))
                throw new ArgumentException("SQL filename cannot be null/empty.", nameof(fileNameInArchive));

            // If already present, done.
            if (SecureEncryptedDataStore.HasKey(fileNameInArchive))
                return;

            EnsureKeysetLoadedOnce();

            if (!SecureEncryptedDataStore.HasKey(fileNameInArchive))
            {
                throw new FileNotFoundException(
                    $"SQL artifact '{fileNameInArchive}' was not found in SEDS after loading keyset.json. " +
                    $"Check the key archive contents and the required SQL catalog names.",
                    fileNameInArchive);
            }
        }

        /// <summary>
        /// Minimal loader used by validators. Reads raw bytes of keyset.json from the encrypted archive.
        /// Returns Array.Empty&lt;byte&gt; on failure.
        /// </summary>
        public byte[] LoadKeysetJsonBytes()
        {
            char[]? keyPw = null;
            string? pwString = null;

            try
            {
                string archivePath = GetRequiredArchivePathFromSeds();
                if (!File.Exists(archivePath))
                    return Array.Empty<byte>();

                keyPw = GetRequiredKeyPasswordFromSeds();
                pwString = new string(keyPw);

                using var extractor = SevenZipCore.CreateExtractor(archivePath, pwString);

                var entry = extractor.ArchiveFileData.FirstOrDefault(f =>
                    !f.IsDirectory &&
                    (string.Equals(f.FileName, KeysetJsonName, StringComparison.Ordinal) ||
                     string.Equals(Path.GetFileName(f.FileName), KeysetJsonName, StringComparison.Ordinal)));

                if (string.IsNullOrWhiteSpace(entry.FileName))
                    return Array.Empty<byte>();

                using var ms = new MemoryStream();
                extractor.ExtractFile(entry.Index, ms);
                return ms.ToArray();
            }
            catch
            {
                return Array.Empty<byte>();
            }
            finally
            {
                if (pwString != null) SensitiveDataCleaner.WipeString(ref pwString);
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

                EnsureKeySetFromArchive();
                // EnsureKeySetFromArchive sets _keysetLoaded=1 on success.
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

        private static string? TryGetSchemaSqlFromSedsOrDisk()
        {
            // Prefer SEDS.
            if (SecureEncryptedDataStore.HasKey(SchemaFileName))
            {
                var fromSeds = SecureEncryptedDataStore.GetString(SchemaFileName);
                if (!string.IsNullOrWhiteSpace(fromSeds))
                    return fromSeds;
            }

            // Fallback to disk staging.
            var schemaPath = Path.Combine(SqlFolder, SchemaFileName);
            if (File.Exists(schemaPath))
                return File.ReadAllText(schemaPath, Encoding.UTF8);

            return null;
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

        private static Dictionary<string, string> LoadSqlMapFromStagingOrSeds()
        {
            var sqlMap = new Dictionary<string, string>(StringComparer.Ordinal);

            // 1) Prefer staging folder if it exists and has sql
            if (Directory.Exists(SqlFolder))
            {
                foreach (var file in Directory.EnumerateFiles(SqlFolder, "*.sql", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileName(file);
                    var text = File.ReadAllText(file, Encoding.UTF8);
                    sqlMap[name] = text ?? string.Empty;
                }
            }

            if (sqlMap.Count > 0)
                return sqlMap;

            // 2) Fallback: if schema already in SEDS, at least embed that.
            //    (If you later build a SqlCatalog, this can be replaced with enumerating catalog names.)
            if (SecureEncryptedDataStore.HasKey(SchemaFileName))
            {
                var schema = SecureEncryptedDataStore.GetString(SchemaFileName) ?? string.Empty;
                sqlMap[SchemaFileName] = schema;
            }

            return sqlMap;
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

        private static void TrySecureDeleteTempDir(string tempDir)
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
            catch
            {
                // best effort
            }
        }

        private static void SecurelyScrubSqlStagingFolder()
        {
            if (!Directory.Exists(SqlFolder))
                return;

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
            catch
            {
                // best effort
            }
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
