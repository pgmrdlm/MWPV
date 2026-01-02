// File: Services/CategoryItemService.cs
//
// COMPLETE REWRITE (same responsibilities, but:
// - Single, consistent SQL loading choke point
// - Clean, explicit transaction boundaries
// - Inserts logs for successful inserts (Item created + Password history created)
// - Logs are "best effort" and NEVER break the insert UX
// - No sensitive values are logged (only ids + boolean “fields present” flags)
//
// CHANGE IN THIS REWRITE:
// - Added public InsertCategoryItemOnly(...) wrapper for bookmark-only flow
//   (inserts CategoryItem WITHOUT launching PasswordHistory insert)
//
// CHANGE NOW (THIS TASK):
// - Wired CI_BookMarkOnly through the insert SQL + service layer.

using Microsoft.Data.Sqlite;
using MWPV.Models;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using MWPV.Services;       // LogCatalogService
using Utilities.Helpers;   // DatabaseHelper, ErrorHandler
using Utilities.Sql;       // SqlCagegory (SQL catalog/loader)
using MWPV.Utilities.Json; // AppJson (LogPayloadDto + serializer helpers)

namespace MWPV.Services
{
    public static class CategoryItemService
    {
        // ============================================================
        // SQL loading (single choke point)
        // ============================================================

        private static string LoadSqlRequired(string assetName)
        {
            var sql = SqlCagegory.GetSql(assetName);
            if (string.IsNullOrWhiteSpace(sql))
                throw new InvalidOperationException($"SQL not loaded: {assetName}");
            return sql;
        }

        // ============================================================
        // SELECT: Category Items grid
        // ============================================================

        /// <summary>Loads items for a category into the 3-column grid row model.</summary>
        public static ObservableCollection<CategoryItemGriud> LoadCategoryItems(int categoryKey)
        {
            if (categoryKey < 0)
                throw new ArgumentOutOfRangeException(nameof(categoryKey), "categoryKey cannot be negative.");

            var rows = new ObservableCollection<CategoryItemGriud>();

            try
            {
                var sql = LoadSqlRequired("s_CategoryItem_SelectGrid.sql");

#if DEBUG
                Debug.WriteLine("[SQL][TEXT] >>> s_CategoryItem_SelectGrid.sql");
                Debug.WriteLine(sql);
                Debug.WriteLine("[SQL][TEXT] <<<");
#endif

                using var conn = DatabaseHelper.GetAppOpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;

                AddInt32(cmd, "@Category_Key", categoryKey);

#if DEBUG
                Debug.WriteLine($"[SQL][PARAM] @Category_Key = {categoryKey} (type=Int32)");
#endif

                using var r = cmd.ExecuteReader();

                // Expected aliases:
                // Key1/Key2/Key3, Col1/Col2/Col3, Des1/Des2/Des3
                var iKey1 = r.GetOrdinal("Key1");
                var iKey2 = r.GetOrdinal("Key2");
                var iKey3 = r.GetOrdinal("Key3");
                var iCol1 = r.GetOrdinal("Col1");
                var iCol2 = r.GetOrdinal("Col2");
                var iCol3 = r.GetOrdinal("Col3");
                var iDes1 = r.GetOrdinal("Des1");
                var iDes2 = r.GetOrdinal("Des2");
                var iDes3 = r.GetOrdinal("Des3");

                while (r.Read())
                {
                    rows.Add(new CategoryItemGriud
                    {
                        strCategoryItemKey1 = r.IsDBNull(iKey1) ? "" : r.GetValue(iKey1)?.ToString() ?? "",
                        strCategoryItemKey2 = r.IsDBNull(iKey2) ? "" : r.GetValue(iKey2)?.ToString() ?? "",
                        strCategoryItemKey3 = r.IsDBNull(iKey3) ? "" : r.GetValue(iKey3)?.ToString() ?? "",

                        strCategoryItem1 = r.IsDBNull(iCol1) ? "" : r.GetString(iCol1),
                        strCategoryItem2 = r.IsDBNull(iCol2) ? "" : r.GetString(iCol2),
                        strCategoryItem3 = r.IsDBNull(iCol3) ? "" : r.GetString(iCol3),

                        strCategoryItemToolTip1 = r.IsDBNull(iDes1) ? "" : r.GetString(iDes1),
                        strCategoryItemToolTip2 = r.IsDBNull(iDes2) ? "" : r.GetString(iDes2),
                        strCategoryItemToolTip3 = r.IsDBNull(iDes3) ? "" : r.GetString(iDes3),
                    });
                }

#if DEBUG
                Debug.WriteLine($"[CAT_ITEMS][LOAD] rows={rows.Count} for CatKey={categoryKey}");
#endif
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error loading category items");
            }

            return rows;
        }

        // ============================================================
        // INSERT: CategoryItem ONLY (bookmark-only flow)
        // ============================================================

        /// <summary>
        /// Inserts CategoryItem only (NO PasswordHistory insert).
        /// Returns the new ItemId (> 0) on success.
        /// </summary>
        public static long InsertCategoryItemOnly(
            int categoryKey,
            string? name,
            string? description,
            string? username,
            string? signInUrl,
            string? accountEmail,
            string? accountPhoneNumber,
            byte[]? secretMeta,      // -> CI_SecretMeta (BLOB)
            byte[]? secretData,      // -> CI_SecretData (BLOB)
            int? secretStorage,      // -> CI_SecretStorage (INTEGER NOT NULL, CHECK 0/1/2)
            int? isActive            // -> IsActive (nullable or defaulted in SQL)
        )
        {
            if (categoryKey < 0)
                throw new ArgumentOutOfRangeException(nameof(categoryKey), "categoryKey cannot be negative.");
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("name is required.", nameof(name));

            try
            {
                var itemSql = LoadSqlRequired("s_CategoryItem_insert.sql");

#if DEBUG
                Debug.WriteLine("[SQL][TEXT] >>> s_CategoryItem_insert.sql");
                Debug.WriteLine(itemSql);
                Debug.WriteLine("[SQL][TEXT] <<<");
#endif

                using var conn = DatabaseHelper.GetAppOpenConnection();
                using var tx = conn.BeginTransaction();

                long newItemId = 0;

                try
                {
                    // Bookmark-only flow => CI_BookMarkOnly = 1
                    newItemId = InsertCategoryItem(
                        conn, tx, itemSql,
                        categoryKey,
                        name!.Trim(),
                        description,
                        username,
                        signInUrl,
                        bookMarkOnly: 1,
                        accountEmail,
                        accountPhoneNumber,
                        secretMeta,
                        secretData,
                        secretStorage,
                        isActive);

                    if (newItemId <= 0)
                        throw new InvalidOperationException("Insert failed (no ItemId returned)");

                    tx.Commit();

#if DEBUG
                    Debug.WriteLine($"[CAT_ITEM][INSERT-ONLY][OK] ItemId={newItemId} CatKey={categoryKey} BookMarkOnly=1");
#endif
                }
                catch
                {
                    try { tx.Rollback(); } catch { /* swallow */ }
                    throw;
                }

                // Best-effort log AFTER commit (never blocks UX)
                BestEffortLogNewItemCreatedOnly(
                    itemId: newItemId,
                    categoryKey: categoryKey,
                    namePresent: true,
                    descriptionPresent: !string.IsNullOrWhiteSpace(description),
                    usernamePresent: !string.IsNullOrWhiteSpace(username),
                    urlPresent: !string.IsNullOrWhiteSpace(signInUrl),
                    emailPresent: !string.IsNullOrWhiteSpace(accountEmail),
                    phonePresent: !string.IsNullOrWhiteSpace(accountPhoneNumber),
                    secretMetaPresent: secretMeta != null && secretMeta.Length > 0,
                    secretDataPresent: secretData != null && secretData.Length > 0,
                    secretStorage: NormalizeSecretStorage(secretStorage),
                    isActive: isActive,
                    bookMarkOnly: 1);

                return newItemId;
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error inserting category item (bookmark-only / no password history)");
                return 0;
            }
        }

        // ============================================================
        // INSERT: CategoryItem + PasswordHistory (single transaction)
        // ============================================================

        /// <summary>
        /// Inserts CategoryItem and the first PasswordHistory row in a single transaction.
        /// Returns the new ItemId (> 0) on success.
        /// </summary>
        public static long InsertCategoryItemWithPasswordHistory(
            int categoryKey,
            string? name,
            string? description,
            string? username,
            string? signInUrl,
            string? accountEmail,
            string? accountPhoneNumber,
            byte[]? secretMeta,      // -> CI_SecretMeta (BLOB)
            byte[]? secretData,      // -> CI_SecretData (BLOB)
            int? secretStorage,      // -> CI_SecretStorage (INTEGER NOT NULL, CHECK 0/1/2)
            int? isActive,           // -> IsActive (nullable or defaulted in SQL)
            byte[] pwCipher,         // -> CIPaH_Password (BLOB NOT NULL)
            int? pwPadLen,           // -> CIPaH_PadLen (INTEGER)
            byte[] pwSig             // -> CIPaH_PwSig (BLOB NOT NULL)
        )
        {
            // ---- Guards (keep these cheap + obvious)
            if (categoryKey < 0)
                throw new ArgumentOutOfRangeException(nameof(categoryKey), "categoryKey cannot be negative.");
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("name is required.", nameof(name));
            if (pwCipher is null || pwCipher.Length == 0)
                throw new ArgumentException("pwCipher is required.", nameof(pwCipher));
            if (pwSig is null || pwSig.Length == 0)
                throw new ArgumentException("pwSig is required.", nameof(pwSig));

            try
            {
                var itemSql = LoadSqlRequired("s_CategoryItem_insert.sql");
                var pwSql = LoadSqlRequired("s_CategoryItemPasswordHistory_insert.sql");

#if DEBUG
                Debug.WriteLine("[SQL][TEXT] >>> s_CategoryItem_insert.sql");
                Debug.WriteLine(itemSql);
                Debug.WriteLine("[SQL][TEXT] <<<");
                Debug.WriteLine("[SQL][TEXT] >>> s_CategoryItemPasswordHistory_insert.sql");
                Debug.WriteLine(pwSql);
                Debug.WriteLine("[SQL][TEXT] <<<");
#endif

                using var conn = DatabaseHelper.GetAppOpenConnection();
                using var tx = conn.BeginTransaction();

                long newItemId = 0;
                long newPwHistId = 0;

                try
                {
                    // Normal password flow => CI_BookMarkOnly = 0
                    newItemId = InsertCategoryItem(
                        conn, tx, itemSql,
                        categoryKey,
                        name!.Trim(),
                        description,
                        username,
                        signInUrl,
                        bookMarkOnly: 0,
                        accountEmail,
                        accountPhoneNumber,
                        secretMeta,
                        secretData,
                        secretStorage,
                        isActive);

                    if (newItemId <= 0)
                        throw new InvalidOperationException("Insert failed (no ItemId returned)");

                    // 2) Insert PasswordHistory (must return PwHistId)
                    newPwHistId = InsertPasswordHistory(
                        conn, tx, pwSql,
                        newItemId,
                        pwCipher,
                        pwPadLen,
                        pwSig);

                    if (newPwHistId <= 0)
                        throw new InvalidOperationException("PasswordHistory insert failed (no PwHistId returned)");

                    // 3) Commit DB work
                    tx.Commit();

#if DEBUG
                    Debug.WriteLine($"[CAT_ITEM][INSERT][OK] ItemId={newItemId} CatKey={categoryKey} BookMarkOnly=0");
                    Debug.WriteLine($"[CAT_ITEM][PW_HIST][INSERT][OK] PwHistId={newPwHistId} ItemId={newItemId}");
#endif
                }
                catch
                {
                    try { tx.Rollback(); } catch { /* swallow */ }
                    throw;
                }

                // 4) Best-effort logs AFTER commit (so logs never block the insert)
                BestEffortLogNewItem(
                    itemId: newItemId,
                    categoryKey: categoryKey,
                    namePresent: true,
                    descriptionPresent: !string.IsNullOrWhiteSpace(description),
                    usernamePresent: !string.IsNullOrWhiteSpace(username),
                    urlPresent: !string.IsNullOrWhiteSpace(signInUrl),
                    emailPresent: !string.IsNullOrWhiteSpace(accountEmail),
                    phonePresent: !string.IsNullOrWhiteSpace(accountPhoneNumber),
                    secretMetaPresent: secretMeta != null && secretMeta.Length > 0,
                    secretDataPresent: secretData != null && secretData.Length > 0,
                    secretStorage: NormalizeSecretStorage(secretStorage),
                    isActive: isActive,
                    pwHistId: newPwHistId,
                    bookMarkOnly: 0);

                return newItemId;
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error inserting category item + password history");
                return 0;
            }
        }

        // ============================================================
        // INSERT helpers
        // ============================================================

        private static long InsertCategoryItem(
            SqliteConnection conn,
            SqliteTransaction tx,
            string sql,
            int categoryKey,
            string name,
            string? description,
            string? username,
            string? signInUrl,
            int bookMarkOnly,        // 0/1 -> CI_BookMarkOnly
            string? accountEmail,
            string? accountPhoneNumber,
            byte[]? secretMeta,
            byte[]? secretData,
            int? secretStorage,
            int? isActive
        )
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;

            // IMPORTANT: parameter names MUST match s_CategoryItem_insert.sql exactly
            AddInt32(cmd, "@Category_Key", categoryKey);
            AddText(cmd, "@CI_Name", name); // never null
            AddText(cmd, "@CI_Description", description);
            AddText(cmd, "@CI_Username", username);
            AddText(cmd, "@CI_SignInUrl", signInUrl);

            AddInt32(cmd, "@CI_BookMarkOnly", NormalizeBookMarkOnly(bookMarkOnly));

            AddText(cmd, "@CI_AccountEmail", accountEmail);
            AddText(cmd, "@CI_AccountPhoneNumber", accountPhoneNumber);

            AddBlob(cmd, "@CI_SecretMeta", secretMeta);
            AddBlob(cmd, "@CI_SecretData", secretData);

            // NOT NULL w/ CHECK -> never send NULL
            AddInt32(cmd, "@CI_SecretStorage", NormalizeSecretStorage(secretStorage));

            // Nullable if SQL/schema allow
            AddInt32Nullable(cmd, "@IsActive", isActive);

#if DEBUG
            DebugDumpParams(cmd, "[CAT_ITEM][INSERT][PARAMS]");
#endif

            // Script MUST return scalar ItemId (RETURNING ItemId or SELECT last_insert_rowid())
            var scalar = cmd.ExecuteScalar();
            if (scalar == null || scalar == DBNull.Value)
                throw new InvalidOperationException("Insert failed (no ItemId returned)");

            return Convert.ToInt64(scalar);
        }

        /// <summary>
        /// Inserts first row into CategoryItemPasswordHistory (CIPaH_* schema).
        /// Expects SQL to end with: RETURNING CIPaH_PwHistId;
        /// </summary>
        private static long InsertPasswordHistory(
            SqliteConnection conn,
            SqliteTransaction tx,
            string sql,
            long itemId,
            byte[] pwCipher,
            int? pwPadLen,
            byte[] pwSig
        )
        {
            if (itemId <= 0)
                throw new ArgumentOutOfRangeException(nameof(itemId), "itemId must be > 0 for password history insert.");

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;

            // IMPORTANT: parameter names MUST match s_CategoryItemPasswordHistory_insert.sql exactly
            AddInt64(cmd, "@CIPaH_ItemId", itemId);
            AddBlob(cmd, "@CIPaH_Password", pwCipher);
            AddInt32Nullable(cmd, "@CIPaH_PadLen", pwPadLen);
            AddBlob(cmd, "@CIPaH_PwSig", pwSig);

#if DEBUG
            DebugDumpParams(cmd, "[CAT_ITEM][PW_HIST][INSERT][PARAMS]");
#endif

            var scalar = cmd.ExecuteScalar();
            if (scalar == null || scalar == DBNull.Value)
                throw new InvalidOperationException("PasswordHistory insert failed (no PwHistId returned)");

            return Convert.ToInt64(scalar);
        }

        // ============================================================
        // Logging (best effort, no secrets)
        // ============================================================

        private static void BestEffortLogNewItemCreatedOnly(
            long itemId,
            int categoryKey,
            bool namePresent,
            bool descriptionPresent,
            bool usernamePresent,
            bool urlPresent,
            bool emailPresent,
            bool phonePresent,
            bool secretMetaPresent,
            bool secretDataPresent,
            int secretStorage,
            int? isActive,
            int bookMarkOnly)
        {
            try
            {
                var createdDto = new AppJson.LogPayloadDto
                {
                    Message = "Category item created (bookmark-only / no password history)",
                    Source = "CategoryItemService",
                    EventCode = "CATEGORYITEM_CREATED_BOOKMARK_ONLY",
                    OccurredUtc = DateTime.UtcNow,
                    Context = BuildContext(new
                    {
                        itemId,
                        categoryKey,
                        bookMarkOnly,
                        fieldsPresent = new
                        {
                            name = namePresent,
                            description = descriptionPresent,
                            username = usernamePresent,
                            url = urlPresent,
                            email = emailPresent,
                            phone = phonePresent,
                            secretMeta = secretMetaPresent,
                            secretData = secretDataPresent
                        },
                        secretStorage,
                        isActive
                    })
                };

                LogCatalogService.AppendJson(
                    level: "INFO",
                    source: "CategoryItem",
                    eventCode: "CATEGORYITEM_CREATED_BOOKMARK_ONLY",
                    dto: createdDto,
                    whenUtc: DateTime.UtcNow,
                    itemId: itemId);
            }
            catch
            {
                // Never allow logging to break insertion UX
            }
        }

        private static void BestEffortLogNewItem(
            long itemId,
            int categoryKey,
            bool namePresent,
            bool descriptionPresent,
            bool usernamePresent,
            bool urlPresent,
            bool emailPresent,
            bool phonePresent,
            bool secretMetaPresent,
            bool secretDataPresent,
            int secretStorage,
            int? isActive,
            long pwHistId,
            int bookMarkOnly)
        {
            try
            {
                // Event 1: Item created (name set)
                var createdDto = new AppJson.LogPayloadDto
                {
                    Message = "Category item created",
                    Source = "CategoryItemService",
                    EventCode = "CATEGORYITEM_NAME_ADDED",
                    OccurredUtc = DateTime.UtcNow,
                    Context = BuildContext(new
                    {
                        itemId,
                        categoryKey,
                        bookMarkOnly,
                        fieldsPresent = new
                        {
                            name = namePresent,
                            description = descriptionPresent,
                            username = usernamePresent,
                            url = urlPresent,
                            email = emailPresent,
                            phone = phonePresent,
                            secretMeta = secretMetaPresent,
                            secretData = secretDataPresent
                        },
                        secretStorage,
                        isActive
                    })
                };

                LogCatalogService.AppendJson(
                    level: "INFO",
                    source: "CategoryItem",
                    eventCode: "CATEGORYITEM_NAME_ADDED",
                    dto: createdDto,
                    whenUtc: DateTime.UtcNow,
                    itemId: itemId);

                // Event 2: Password set (history row created)
                var pwDto = new AppJson.LogPayloadDto
                {
                    Message = "Category item password set (history row created)",
                    Source = "CategoryItemService",
                    EventCode = "CATEGORYITEM_PASSWORD_CHANGED",
                    OccurredUtc = DateTime.UtcNow,
                    Context = BuildContext(new
                    {
                        itemId,
                        pwHistId
                    })
                };

                LogCatalogService.AppendJson(
                    level: "INFO",
                    source: "CategoryItem",
                    eventCode: "CATEGORYITEM_PASSWORD_CHANGED",
                    dto: pwDto,
                    whenUtc: DateTime.UtcNow,
                    itemId: itemId);
            }
            catch
            {
                // Never allow logging to break insertion UX
            }
        }

        private static System.Text.Json.JsonElement? BuildContext(object obj)
        {
            try
            {
                var json = AppJson.Serialize(obj, pretty: false);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                return doc.RootElement.Clone();
            }
            catch
            {
                return null;
            }
        }

        // ============================================================
        // Normalization / validation
        // ============================================================

        private static int NormalizeSecretStorage(int? secretStorage)
        {
            // DB column is NOT NULL. Legal values: 0/1/2 only.
            if (!secretStorage.HasValue)
                return 0;

            return secretStorage.Value switch
            {
                0 => 0,
                1 => 1,
                2 => 2,
                _ => throw new ArgumentOutOfRangeException(nameof(secretStorage), secretStorage.Value,
                        "CI_SecretStorage must be 0, 1, or 2.")
            };
        }

        private static int NormalizeBookMarkOnly(int value)
        {
            // CI_BookMarkOnly is intended to be 0/1.
            return value switch
            {
                0 => 0,
                1 => 1,
                _ => throw new ArgumentOutOfRangeException(nameof(value), value, "CI_BookMarkOnly must be 0 or 1.")
            };
        }

        // ============================================================
        // Param helpers (single place, avoids drift)
        // ============================================================

        private static void AddText(SqliteCommand cmd, string name, string? value)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.SqliteType = SqliteType.Text;
            p.Value = (object?)value ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        private static void AddBlob(SqliteCommand cmd, string name, byte[]? value)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.SqliteType = SqliteType.Blob;
            p.Value = (object?)value ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        private static void AddInt32(SqliteCommand cmd, string name, int value)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.SqliteType = SqliteType.Integer;
            p.Value = value;
            cmd.Parameters.Add(p);
        }

        private static void AddInt32Nullable(SqliteCommand cmd, string name, int? value)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.SqliteType = SqliteType.Integer;
            p.Value = (object?)value ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        private static void AddInt64(SqliteCommand cmd, string name, long value)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.SqliteType = SqliteType.Integer;
            p.Value = value;
            cmd.Parameters.Add(p);
        }

#if DEBUG
        private static void DebugDumpParams(SqliteCommand cmd, string tag)
        {
            Debug.WriteLine(tag);
            foreach (SqliteParameter p in cmd.Parameters)
            {
                string v = (p.Value == null || p.Value == DBNull.Value)
                    ? "NULL"
                    : (p.Value is byte[] b ? $"BLOB[{b.Length}]" : p.Value.ToString() ?? "");

                Debug.WriteLine($"  {p.ParameterName} = {v} (SqliteType={p.SqliteType})");
            }
        }
#endif
    }
}
