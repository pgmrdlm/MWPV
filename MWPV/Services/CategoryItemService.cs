// File: Services/CategoryItemService.cs
//
// FULL REWRITE (with backwards-compatible named parameters)
//
// Fix for CS1739:
// - Adds overloads whose parameter NAMES match the UI call sites:
//     accountEmail, accountPhoneNumber
// - Internally we treat these as encrypted bytes (BLOB) and forward to the core impl.
//
// Notes:
// - Password stays in CategoryItemPasswordHistory (NOT CategoryItem).
// - Bookmark-only => inserts CategoryItem only (no PasswordHistory row).
// - Logging is best-effort and never logs secrets (only “present” flags).

using Microsoft.Data.Sqlite;
using MWPV.Models;
using MWPV.Services;       // LogCatalogService
using MWPV.Utilities.Json; // AppJson
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Utilities.Helpers;   // DatabaseHelper, ErrorHandler
using Utilities.Sql;       // SqlCagegory (SQL catalog/loader)

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

        // --- Back-compat overload: matches UI named args (accountEmail/accountPhoneNumber)
        public static long InsertCategoryItemOnly(
            int categoryKey,
            string name,
            string? description,
            string? username,
            string? signInUrl,
            byte[]? accountEmail,
            byte[]? accountPhoneNumber,
            byte[]? pin = null,
            int? isActive = null
        )
        {
            return InsertCategoryItemOnlyCore(
                categoryKey: categoryKey,
                name: name,
                description: description,
                username: username,
                signInUrl: signInUrl,
                accountEmailCipher: accountEmail,
                accountPhoneCipher: accountPhoneNumber,
                pinCipher: pin,
                isActive: isActive);
        }

        /// <summary>
        /// Inserts CategoryItem only (NO PasswordHistory insert).
        /// Values for masked fields must be encrypted bytes (BLOBs).
        /// </summary>
        public static long InsertCategoryItemOnlyCore(
            int categoryKey,
            string name,
            string? description,
            string? username,
            string? signInUrl,
            byte[]? accountEmailCipher,
            byte[]? accountPhoneCipher,
            byte[]? pinCipher,
            int? isActive = null
        )
        {
            if (categoryKey < 0)
                throw new ArgumentOutOfRangeException(nameof(categoryKey), "categoryKey cannot be negative.");
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("name is required.", nameof(name));

            try
            {
                var itemSql = LoadSqlRequired("s_CategoryItem_insert.sql");

                using var conn = DatabaseHelper.GetAppOpenConnection();
                using var tx = conn.BeginTransaction();

                long newItemId;

                try
                {
                    newItemId = InsertCategoryItemCore(
                        conn, tx, itemSql,
                        categoryKey: categoryKey,
                        name: name.Trim(),
                        description: description,
                        username: username,
                        signInUrl: signInUrl,
                        bookMarkOnly: 1,
                        accountEmailCipher: accountEmailCipher,
                        accountPhoneCipher: accountPhoneCipher,
                        pinCipher: pinCipher,
                        isActive: isActive);

                    if (newItemId <= 0)
                        throw new InvalidOperationException("Insert failed (no ItemId returned)");

                    tx.Commit();
                }
                catch
                {
                    try { tx.Rollback(); } catch { /* swallow */ }
                    throw;
                }

                BestEffortLogItemCreated(
                    itemId: newItemId,
                    categoryKey: categoryKey,
                    bookMarkOnly: 1,
                    namePresent: true,
                    descriptionPresent: !string.IsNullOrWhiteSpace(description),
                    usernamePresent: !string.IsNullOrWhiteSpace(username),
                    urlPresent: !string.IsNullOrWhiteSpace(signInUrl),
                    emailPresent: accountEmailCipher is { Length: > 0 },
                    phonePresent: accountPhoneCipher is { Length: > 0 },
                    pinPresent: pinCipher is { Length: > 0 },
                    isActive: isActive);

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

        // --- Back-compat overload: matches UI named args (accountEmail/accountPhoneNumber)
        public static long InsertCategoryItemWithPasswordHistory(
            int categoryKey,
            string name,
            string? description,
            string? username,
            string? signInUrl,
            byte[]? accountEmail,
            byte[]? accountPhoneNumber,
            int? isActive,
            byte[] pwCipher,
            int? pwPadLen,
            byte[] pwSig,
            byte[]? pin = null
        )
        {
            return InsertCategoryItemWithPasswordHistoryCore(
                categoryKey: categoryKey,
                name: name,
                description: description,
                username: username,
                signInUrl: signInUrl,
                accountEmailCipher: accountEmail,
                accountPhoneCipher: accountPhoneNumber,
                pinCipher: pin,
                isActive: isActive,
                pwCipher: pwCipher,
                pwPadLen: pwPadLen,
                pwSig: pwSig);
        }

        /// <summary>
        /// Inserts CategoryItem and first PasswordHistory row in a single transaction.
        /// Values for masked fields must be encrypted bytes (BLOBs).
        /// </summary>
        public static long InsertCategoryItemWithPasswordHistoryCore(
            int categoryKey,
            string name,
            string? description,
            string? username,
            string? signInUrl,
            byte[]? accountEmailCipher,
            byte[]? accountPhoneCipher,
            byte[]? pinCipher,
            int? isActive,
            byte[] pwCipher,
            int? pwPadLen,
            byte[] pwSig
        )
        {
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

                using var conn = DatabaseHelper.GetAppOpenConnection();
                using var tx = conn.BeginTransaction();

                long newItemId;
                long newPwHistId;

                try
                {
                    newItemId = InsertCategoryItemCore(
                        conn, tx, itemSql,
                        categoryKey: categoryKey,
                        name: name.Trim(),
                        description: description,
                        username: username,
                        signInUrl: signInUrl,
                        bookMarkOnly: 0,
                        accountEmailCipher: accountEmailCipher,
                        accountPhoneCipher: accountPhoneCipher,
                        pinCipher: pinCipher,
                        isActive: isActive);

                    if (newItemId <= 0)
                        throw new InvalidOperationException("Insert failed (no ItemId returned)");

                    newPwHistId = InsertPasswordHistoryCore(
                        conn, tx, pwSql,
                        itemId: newItemId,
                        pwCipher: pwCipher,
                        pwPadLen: pwPadLen,
                        pwSig: pwSig);

                    if (newPwHistId <= 0)
                        throw new InvalidOperationException("PasswordHistory insert failed (no PwHistId returned)");

                    tx.Commit();
                }
                catch
                {
                    try { tx.Rollback(); } catch { /* swallow */ }
                    throw;
                }

                // New item creation => item-created log only (no password-changed log here)
                BestEffortLogItemCreated(
                    itemId: newItemId,
                    categoryKey: categoryKey,
                    bookMarkOnly: 0,
                    namePresent: true,
                    descriptionPresent: !string.IsNullOrWhiteSpace(description),
                    usernamePresent: !string.IsNullOrWhiteSpace(username),
                    urlPresent: !string.IsNullOrWhiteSpace(signInUrl),
                    emailPresent: accountEmailCipher is { Length: > 0 },
                    phonePresent: accountPhoneCipher is { Length: > 0 },
                    pinPresent: pinCipher is { Length: > 0 },
                    isActive: isActive);

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

        private static long InsertCategoryItemCore(
            SqliteConnection conn,
            SqliteTransaction tx,
            string sql,
            int categoryKey,
            string name,
            string? description,
            string? username,
            string? signInUrl,
            int bookMarkOnly,              // 0/1
            byte[]? accountEmailCipher,    // BLOB
            byte[]? accountPhoneCipher,    // BLOB
            byte[]? pinCipher,             // BLOB (optional column)
            int? isActive
        )
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;

            // Required
            AddInt32(cmd, "@Category_Key", categoryKey);
            AddText(cmd, "@CI_Name", name);

            // Optional plaintext columns
            AddTextIfSqlUses(cmd, sql, "@CI_Description", description);
            AddTextIfSqlUses(cmd, sql, "@CI_Username", username);
            AddTextIfSqlUses(cmd, sql, "@CI_SignInUrl", signInUrl);

            // Bookmark flag
            AddInt32IfSqlUses(cmd, sql, "@CI_BookMarkOnly", NormalizeBookMarkOnly(bookMarkOnly));

            // Masked/sensitive -> encrypted BLOBs
            AddBlobIfSqlUses(cmd, sql, "@CI_AccountEmail", accountEmailCipher);
            AddBlobIfSqlUses(cmd, sql, "@CI_AccountPhoneNumber", accountPhoneCipher);
            AddBlobIfSqlUses(cmd, sql, "@CI_Pin", pinCipher);

            // Active (DDL default is 1; bind only if SQL references it)
            AddInt32NullableIfSqlUses(cmd, sql, "@IsActive", isActive);

            // Legacy/transition columns (bind only if SQL references them)
            AddBlobIfSqlUses(cmd, sql, "@CI_SecretMeta", null);
            AddBlobIfSqlUses(cmd, sql, "@CI_SecretData", null);
            AddTextIfSqlUses(cmd, sql, "@CI_SecretStorage", null);

#if DEBUG
            DebugDumpParams(cmd, "[CAT_ITEM][INSERT][PARAMS]");
#endif

            var scalar = cmd.ExecuteScalar();
            if (scalar == null || scalar == DBNull.Value)
                throw new InvalidOperationException("Insert failed (no ItemId returned)");

            return Convert.ToInt64(scalar);
        }

        private static long InsertPasswordHistoryCore(
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

        private static void BestEffortLogItemCreated(
            long itemId,
            int categoryKey,
            int bookMarkOnly,
            bool namePresent,
            bool descriptionPresent,
            bool usernamePresent,
            bool urlPresent,
            bool emailPresent,
            bool phonePresent,
            bool pinPresent,
            int? isActive)
        {
            try
            {
                var dto = new AppJson.LogPayloadDto
                {
                    Message = bookMarkOnly == 1
                        ? "Category item created (bookmark-only)"
                        : "Category item created",
                    Source = "CategoryItemService",
                    EventCode = bookMarkOnly == 1
                        ? "CATEGORYITEM_CREATED_BOOKMARK_ONLY"
                        : "CATEGORYITEM_CREATED",
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
                            pin = pinPresent
                        },
                        isActive
                    })
                };

                LogCatalogService.AppendJson(
                    level: "INFO",
                    source: "CategoryItem",
                    eventCode: dto.EventCode ?? "CATEGORYITEM_CREATED",
                    dto: dto,
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
        // Normalization
        // ============================================================

        private static int NormalizeBookMarkOnly(int value)
        {
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

        private static bool SqlUses(string sql, string paramName)
            => sql.IndexOf(paramName, StringComparison.OrdinalIgnoreCase) >= 0;

        private static void AddTextIfSqlUses(SqliteCommand cmd, string sql, string name, string? value)
        {
            if (!SqlUses(sql, name)) return;
            AddText(cmd, name, value);
        }

        private static void AddBlobIfSqlUses(SqliteCommand cmd, string sql, string name, byte[]? value)
        {
            if (!SqlUses(sql, name)) return;
            AddBlob(cmd, name, value);
        }

        private static void AddInt32IfSqlUses(SqliteCommand cmd, string sql, string name, int value)
        {
            if (!SqlUses(sql, name)) return;
            AddInt32(cmd, name, value);
        }

        private static void AddInt32NullableIfSqlUses(SqliteCommand cmd, string sql, string name, int? value)
        {
            if (!SqlUses(sql, name)) return;
            AddInt32Nullable(cmd, name, value);
        }

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
