// Utilities/Sql/SqlCatagory.cs
using System;
using System.Collections.Generic;
using Utilities.Helpers;      // DatabaseHelper.DbPasswordKey
using Security.Utility;     // SecureEncryptedDataStore
using MWPV.Services;          // ServiceSetUp
using Utilities.Security;

namespace Utilities.Sql
{
    /// <summary>
    /// Single source of truth for SQL/secret artifacts stored in keys.7z,
    /// with helpers to load them into the secure store and cache SQL text.
    /// </summary>
    public static class SqlCatagory
    {
        // 📦 Master catalog — update here and nowhere else.
        // Includes SQL scripts + the DB password entry (secret).
        private static readonly string[] RequiredArtifacts =
        {
            "CatagoryExists.sql",
            DatabaseHelper.DbPasswordKey,   // maps to DB_Password.txt in archive (secret, not SQL)
            "InsertCatagory.sql",
            "Logs_Indexes.sql",
            "Logs_Init.sql",
            "Logs_Insert_V2.sql",
            "Logs_Select_Recent.sql",       // NEW
            "Logs_LastInsertId.sql",        // NEW
            "MWPV_DB_Create.sql",
            "SelectCatagories.sql",
            "Logs_Purge_OlderThan.sql",
            "Logs_Exists_BySig.sql"
        };

        // ✅ Must-haves at runtime (SQL only) — used for warnings/verification.
        public static readonly string[] MustHaveScripts =
        {
            "Logs_Insert_V2.sql",
            "Logs_Select_Recent.sql",
            "Logs_Init.sql",
            "Logs_Indexes.sql",
            "SelectCatagories.sql",
            "CatagoryExists.sql",
            "InsertCatagory.sql"
            // Note: DB password is a secret, not SQL; MWPV_DB_Create.sql not required at runtime.
        };

        // In-memory SQL cache by filename (not used for secrets).
        private static readonly Dictionary<string, string> _sqlCache =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Ensure keys are available, load all artifacts from the archive,
        /// then cache SQL text (and verify the DB password secret is present).
        /// </summary>
        public static void EnsureKeysAndLoadAll()
        {
            ServiceSetUp.EnsureKeySetFromArchive();
            LoadAll();                 // push artifacts into SecureEncryptedDataStore
            CacheSqlArtifactsOrWarn(); // build _sqlCache + verify secret presence
        }

        /// <summary>
        /// Loads all listed artifacts from the encrypted archive into the secure store.
        /// (Does not populate _sqlCache; call CacheSqlArtifactsOrWarn() afterward.)
        /// </summary>
        public static void LoadAll()
        {
            foreach (var file in RequiredArtifacts)
                ServiceSetUp.LoadSqlFromEncryptedArchive(file);
        }

        /// <summary>
        /// Populate the in-memory SQL cache from SecureEncryptedDataStore and
        /// verify the DB password secret exists. Emits DEBUG hits/misses.
        /// </summary>
        public static void CacheSqlArtifactsOrWarn()
        {
            int hits = 0, misses = 0;

            foreach (var key in RequiredArtifacts)
            {
                if (!SecureEncryptedDataStore.HasKey(key))
                {
                    System.Diagnostics.Debug.WriteLine($"[SQLCAT][MISS] {key}");
                    misses++;
                    continue;
                }

                // Secret: database password — verify presence only (do not load).
                if (string.Equals(key, DatabaseHelper.DbPasswordKey, StringComparison.OrdinalIgnoreCase))
                {
                    System.Diagnostics.Debug.WriteLine($"[SQLCAT][HIT ] {key} (secret present)");
                    hits++;
                    continue;
                }

                // Normal SQL artifact: cache the text
                try
                {
                    var sqlText = SecureEncryptedDataStore.GetString(key); // non-sensitive helper
                    if (!string.IsNullOrWhiteSpace(sqlText))
                    {
                        _sqlCache[key] = sqlText;
                        System.Diagnostics.Debug.WriteLine($"[SQLCAT][HIT ] {key}");
                        hits++;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[SQLCAT][MISS] {key} (empty)");
                        misses++;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SQLCAT][MISS] {key} (read error: {ex.Message})");
                    misses++;
                }
            }

            // Verify must-haves (SQL scripts needed at runtime)
            foreach (var name in MustHaveScripts)
            {
                if (!_sqlCache.ContainsKey(name))
                    System.Diagnostics.Debug.WriteLine($"[SQLCAT][WARN] Must-have script not cached: {name}");
            }

            System.Diagnostics.Debug.WriteLine($"[SQLCAT] summary: hits={hits} misses={misses} cached={_sqlCache.Count}");
        }

        /// <summary>
        /// Retrieve cached SQL text by filename. Throws if missing to catch drift early.
        /// </summary>
        public static string GetSql(string name)
        {
            if (_sqlCache.TryGetValue(name, out var sql) && !string.IsNullOrWhiteSpace(sql))
                return sql;

            throw new InvalidOperationException($"[SQLCAT] Missing or empty script: {name}");
        }

        /// <summary>
        /// Exposes the catalog as a read-only view (handy for diagnostics/tests).
        /// </summary>
        public static IReadOnlyList<string> List => RequiredArtifacts;

        /// <summary>
        /// Optional: return missing must-have scripts (DEBUG helper).
        /// </summary>
        public static string[] GetMissingMustHaves()
        {
            var missing = new List<string>();
            foreach (var name in MustHaveScripts)
                if (!_sqlCache.ContainsKey(name)) missing.Add(name);
            return missing.ToArray();
        }
    }
}
