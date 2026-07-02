using System;
using System.Collections.Generic;
using Utilities.Helpers;      // DatabaseHelper.DbPasswordKey
using Security.Utility;       // SecureEncryptedDataStore
using MWPV.Services;          // ServiceSetUp
using Utilities.Security;

namespace Utilities.Sql
{
    /// <summary>
    /// Single source of truth for SQL/secret artifacts stored in keys.7z,
    /// with helpers to load them into the secure store and cache SQL text.
    /// Review-only tmp copy: includes DbVersion SQL in the required catalog.
    /// </summary>
    public static class SqlCagegory
    {
        // Master catalog - update here and nowhere else.
        // Includes SQL scripts + the DB password entry (secret).
        private static readonly string[] RequiredArtifacts =
        {
            // Category
            "s_Category_Exists.sql",
            "s_Category_Exists_ExceptId.sql",
            "s_Category_Insert.sql",
            "s_Category_deactivate.sql",
            "s_Category_select_by_id.sql",
            "s_Category_update.sql",
            "s_CategorySelectAll.sql",
            "s_CategorySelectWithActiveItems.sql",

            // Secrets (not SQL) packaged alongside
            DatabaseHelper.DbPasswordKey,          // maps to DB_Password.txt in archive (secret, not SQL)

            // Logs
            "s_Logs_Insert.sql",
            "s_Logs_SelectRecent.sql",
            "s_Logs_LastInsertId.sql",
            "s_Logs_PurgeOlderThan.sql",
            "s_Logs_Exists_BySig.sql",
            "s_Logs_SelectAll.sql",
            "s_Logs_SelectById.sql",
            "s_Logs_SelectPageFilter.sql",
            "s_Logs_SelectPage.sql",

            // Combos
            "s_Combo_LogsDetailSelectByType.sql",
            "s_Combo_CategoryType.sql",

            // Category Items
            "s_CategoryItem_SelectGrid.sql",
            "s_CategoryItem_SelectGrid_all.sql",
            "s_CategoryItem_select_by_id.sql",
            "s_CategoryItem_insert.sql",
            "s_CategoryItem_update.sql",
            "s_Combo_DetailByTypeId.sql",
            "s_CategoryItem_select_by_category.sql",
            "s_CategoryItem_CountActive_Global.sql",
            "s_CategoryItem_CountActive_ByCategory.sql",

            // Category Item Accounts
            "s_CategoryItemAccounts_select_by_itemid.sql",
            "s_CategoryItemAccounts_select_all_by_itemid.sql",
            "s_CategoryItemAccounts_select_primary_by_itemid.sql",
            "s_CategoryItemAccounts_insert.sql",
            "s_CategoryItemAccounts_update.sql",

            // Category Item Security Questions
            "s_CategoryItemSecurityQuestions_select_by_itemid.sql",
            "s_CategoryItemSecurityQuestions_select_all_by_itemid.sql",
            "s_CategoryItemSecurityQuestions_select_by_itemid_and_id.sql",
            "s_CategoryItemSecurityQuestions_insert.sql",
            "s_CategoryItemSecurityQuestions_update.sql",
            "s_CategoryItemSecurityQuestions_deactivate.sql",

            // Category Item Password history
            "s_CategoryItem_exists_by_name.sql",
            "s_CategoryItemPasswordHistory_insert.sql",
            "s_CategoryItemPasswordHistory_select_most_recent.sql",
            "s_CategoryItemPasswordHistory_select_by_item_most_recent_first.sql",
            "s_CategoryItemPasswordHistory_check_reuse_365days.sql",

            // Bank Cards
            "s_BankCard_insert.sql",
            "s_BankCard_update.sql",
            "s_BankCard_select_by_itemid.sql",

            // Log message templates.
            "s_LogMessageTemplate_SelectAll.sql",

            // DbVersion
            "s_DbVersion_select_current.sql",

            // AppSettings
            "s_AppSettings_select.sql",

            // DDL included in archive for completeness
            "MWPV_DB_Create.sql"
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
            CacheSqlArtifactsOrWarn(); // build _sqlCache + verify presence
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
        /// verify that all required artifacts are present. The DB password is
        /// treated as a secret (presence only, not cached).
        /// </summary>
        public static void CacheSqlArtifactsOrWarn()
        {
            int hits = 0, misses = 0;

            foreach (var key in RequiredArtifacts)
            {
                // Secret: database password - verify presence only (do not load).
                if (string.Equals(key, DatabaseHelper.DbPasswordKey, StringComparison.OrdinalIgnoreCase))
                {
                    if (SecureEncryptedDataStore.HasKey(key))
                    {
                        System.Diagnostics.Debug.WriteLine($"[SQLCAT][HIT ] {key} (secret present)");
                        hits++;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[SQLCAT][MISS] {key} (secret missing)");
                        misses++;
                    }
                    continue;
                }

                // Normal SQL artifact: cache the text
                if (!SecureEncryptedDataStore.HasKey(key))
                {
                    System.Diagnostics.Debug.WriteLine($"[SQLCAT][MISS] {key} (not in secure store)");
                    misses++;
                    continue;
                }

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

            // Final summary
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
        /// Optional: return required scripts (non-secret) that are not cached.
        /// </summary>
        public static string[] GetMissingMustHaves()
        {
            var missing = new List<string>();
            foreach (var name in RequiredArtifacts)
            {
                if (string.Equals(name, DatabaseHelper.DbPasswordKey, StringComparison.OrdinalIgnoreCase))
                    continue; // secret is not part of SQL cache

                if (!_sqlCache.ContainsKey(name))
                    missing.Add(name);
            }
            return missing.ToArray();
        }
    }
}
