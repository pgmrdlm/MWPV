// File: MWPV/Services/CategoryService.cs
using Microsoft.Data.Sqlite;
using MWPV.Models;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using Utilities.Helpers;   // DatabaseHelper, ErrorHandler
using Utilities.Sql;      // SqlCagegory  (intentional spelling to match existing)
using Security.Utility;   // InputGuards

namespace MWPV.Services
{
    public enum CategoryViewMode
    {
        InUse = 0,
        AllActive = 1
    }

    public static class CategoryService
    {
        // ===== Centralized limits to keep UI/Backend consistent =====
        private const int MinCategoryNameLength = 4;
        private const int MaxCategoryNameLength = 64; // was 17
        private const int MaxDescriptionLength = 512;
        private const string Sql_CategorySelectAll = "s_CategorySelectAll.sql";
        private const string Sql_CategorySelectWithActiveItems = "s_CategorySelectWithActiveItems.sql";
        private const string Sql_CategoryItemCountActiveGlobal = "s_CategoryItem_CountActive_Global.sql";
        private const string Sql_CategoryItemCountActiveByCategory = "s_CategoryItem_CountActive_ByCategory.sql";
        private const string Sql_CategorySelectById = "s_Category_select_by_id.sql";
        private const string Sql_CategoryUpdate = "s_Category_update.sql";
        private const string Sql_CategoryExistsExceptId = "s_Category_Exists_ExceptId.sql";
        private const string CategoryLogUpdateForm = "CategoryUpdates";
        private const string LogEventCategoryCreated = "CATEGORY_CREATED";
        private const string LogEventCategoryUpdated = "CATEGORY_UPDATED";

        public sealed class CategoryDetail
        {
            public int CategoryKey { get; init; }
            public string Name { get; init; } = string.Empty;
            public string Description { get; init; } = string.Empty;
            public bool IsActive { get; init; }
            public int ActiveItemCount { get; init; }
        }

        public sealed class CategoryChoice
        {
            public int CategoryKey { get; init; }
            public string Name { get; init; } = string.Empty;
        }

        // --------------------------- Helpers ---------------------------------

        private static string LoadSqlRequired(string assetName)
        {
            var sql = SqlCagegory.GetSql(assetName);
            if (string.IsNullOrWhiteSpace(sql))
                throw new InvalidOperationException($"SQL not loaded: {assetName}");
            return sql;
        }

        // ---------------------------- Reads ----------------------------------

        /// <summary>
        /// Loads the 3-column category grid rows.
        /// Expects SQL to project: Key1/Key2/Key3, Col1/Col2/Col3, Des1/Des2/Des3.
        /// Optional Active1/Active2/Active3 columns mark inactive cells for disabled-looking styling.
        /// </summary>
        public static ObservableCollection<Categories> LoadCategories(CategoryViewMode viewMode = CategoryViewMode.InUse)
        {
            var rows = new ObservableCollection<Categories>();

            try
            {
                string selectSqlName = ChooseCategorySelectSqlName(viewMode);
                var selectSql = LoadSqlRequired(selectSqlName);

                using var conn = DatabaseHelper.GetAppOpenConnection();
                using var cmd = conn.CreateCommand();
                ConfigureCategorySelectCommand(cmd, selectSqlName, selectSql);

                using var r = cmd.ExecuteReader();

                int iKey1 = SafeGetOrdinal(r, "Key1");
                int iKey2 = SafeGetOrdinal(r, "Key2");
                int iKey3 = SafeGetOrdinal(r, "Key3");

                int iCol1 = SafeGetOrdinal(r, "Col1");
                int iCol2 = SafeGetOrdinal(r, "Col2");
                int iCol3 = SafeGetOrdinal(r, "Col3");

                int iDes1 = SafeGetOrdinal(r, "Des1");
                int iDes2 = SafeGetOrdinal(r, "Des2");
                int iDes3 = SafeGetOrdinal(r, "Des3");

                int iActive1 = SafeGetOrdinal(r, "Active1");
                int iActive2 = SafeGetOrdinal(r, "Active2");
                int iActive3 = SafeGetOrdinal(r, "Active3");

                while (r.Read())
                {
                    var row = new Categories
                    {
                        intCategoryKey1 = ReadNullableInt(r, iKey1),
                        intCategoryKey2 = ReadNullableInt(r, iKey2),
                        intCategoryKey3 = ReadNullableInt(r, iKey3),

                        strCategory1 = ReadNullableString(r, iCol1),
                        strCategory2 = ReadNullableString(r, iCol2),
                        strCategory3 = ReadNullableString(r, iCol3),

                        strCategoryToolTip1 = ReadNullableString(r, iDes1),
                        strCategoryToolTip2 = ReadNullableString(r, iDes2),
                        strCategoryToolTip3 = ReadNullableString(r, iDes3),

                        IsActive1 = ReadNullableBoolDefaultTrue(r, iActive1),
                        IsActive2 = ReadNullableBoolDefaultTrue(r, iActive2),
                        IsActive3 = ReadNullableBoolDefaultTrue(r, iActive3),
                    };

                    rows.Add(row);
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error loading categories");
            }

            return rows;
        }

        private static string ChooseCategorySelectSqlName(CategoryViewMode viewMode)
        {
            return viewMode == CategoryViewMode.AllActive
                ? Sql_CategorySelectAll
                : Sql_CategorySelectWithActiveItems;
        }

        private static long CountActiveCategoryItemsGlobal()
        {
            var sql = LoadSqlRequired(Sql_CategoryItemCountActiveGlobal);

            using var conn = DatabaseHelper.GetAppOpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;

            object? scalar = cmd.ExecuteScalar();
            if (scalar == null || scalar == DBNull.Value)
                return 0;

            return Convert.ToInt64(scalar);
        }

        private static int CountActiveCategoryItemsByCategory(int categoryKey)
        {
            if (categoryKey <= 0)
                return 0;

            var sql = LoadSqlRequired(Sql_CategoryItemCountActiveByCategory);

            using var conn = DatabaseHelper.GetAppOpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@CategoryKey", categoryKey);

            object? scalar = cmd.ExecuteScalar();
            if (scalar == null || scalar == DBNull.Value)
                return 0;

            return Convert.ToInt32(scalar);
        }

        public static CategoryDetail? LoadCategoryByKey(int categoryKey)
        {
            if (categoryKey <= 0)
                return null;

            var sql = LoadSqlRequired(Sql_CategorySelectById);

            using var conn = DatabaseHelper.GetAppOpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@CategoryKey", categoryKey);

            using var r = cmd.ExecuteReader();
            if (!r.Read())
                return null;

            int iKey = SafeGetOrdinal(r, "CategoryKey");
            int iName = SafeGetOrdinal(r, "CategoryName");
            int iDescription = SafeGetOrdinal(r, "CategoryDescription");
            int iActive = SafeGetOrdinal(r, "IsActive");

            int resolvedCategoryKey = ReadNullableInt(r, iKey) ?? categoryKey;

            return new CategoryDetail
            {
                CategoryKey = resolvedCategoryKey,
                Name = ReadNullableString(r, iName) ?? string.Empty,
                Description = ReadNullableString(r, iDescription) ?? string.Empty,
                IsActive = (ReadNullableInt(r, iActive) ?? 1) != 0,
                ActiveItemCount = CountActiveCategoryItemsByCategory(resolvedCategoryKey)
            };
        }

        public static IReadOnlyList<CategoryChoice> LoadActiveCategoryChoices()
        {
            var choices = new List<CategoryChoice>();

            using var conn = DatabaseHelper.GetAppOpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT
    Category_Key  AS CategoryKey,
    Category_Name AS CategoryName
FROM Category
WHERE IFNULL(IsActive, 1) = 1
ORDER BY Category_Name COLLATE NOCASE;";

            using var r = cmd.ExecuteReader();
            int iKey = SafeGetOrdinal(r, "CategoryKey");
            int iName = SafeGetOrdinal(r, "CategoryName");

            while (r.Read())
            {
                int? key = ReadNullableInt(r, iKey);
                string? name = ReadNullableString(r, iName);

                if (key.HasValue && key.Value > 0 && !string.IsNullOrWhiteSpace(name))
                {
                    choices.Add(new CategoryChoice
                    {
                        CategoryKey = key.Value,
                        Name = name.Trim()
                    });
                }
            }

            return choices;
        }

        private static void ConfigureCategorySelectCommand(SqliteCommand cmd, string selectSqlName, string selectSql)
        {
            if (!string.Equals(selectSqlName, Sql_CategorySelectWithActiveItems, StringComparison.OrdinalIgnoreCase))
            {
                cmd.CommandText = selectSql;
                return;
            }

            var sessionCategoryKeys = CategorySessionStateService.GetSessionVisibleCategoryKeys();
            if (sessionCategoryKeys.Count == 0)
            {
                cmd.CommandText = selectSql;
                return;
            }

            cmd.CommandText = BuildCategorySelectWithSessionVisibleSql(cmd, sessionCategoryKeys);
        }

        private static string BuildCategorySelectWithSessionVisibleSql(
            SqliteCommand cmd,
            IReadOnlyList<int> sessionCategoryKeys)
        {
            var parameterNames = new List<string>(sessionCategoryKeys.Count);

            for (int i = 0; i < sessionCategoryKeys.Count; i++)
            {
                string parameterName = $"@SessionCategoryKey{i}";
                parameterNames.Add(parameterName);
                cmd.Parameters.AddWithValue(parameterName, sessionCategoryKeys[i]);
            }

            string inClause = string.Join(", ", parameterNames);

            // Intentional one-time exception to the normal MWPV SQL-file pattern:
            // this keeps newly-created categories visible only during the current
            // session, until they gain active items or the app exits. Category_Key
            // values come only from successful category inserts in this process.
            // Values must be bound as SQL parameters. Do not copy this as a
            // general SQL construction pattern elsewhere.
            return $@"
WITH Numbered AS (
    SELECT
        c.Category_Key,
        c.Category_Name,
        c.Category_Description,
        ROW_NUMBER() OVER (ORDER BY c.Category_Name COLLATE NOCASE) - 1 AS rn
    FROM Category c
    WHERE IFNULL(c.IsActive, 1) = 1
      AND (
          EXISTS (
              SELECT 1
              FROM CategoryItem ci
              WHERE ci.Category_Key = c.Category_Key
                AND IFNULL(ci.IsActive, 1) = 1
          )
          OR c.Category_Key IN ({inClause})
      )
),
Grouped AS (
    SELECT
        (rn / 3) AS group_id,
        rn % 3 AS col_pos,
        Category_Key,
        Category_Name,
        Category_Description
    FROM Numbered
)
SELECT
    MAX(CASE WHEN col_pos = 0 THEN Category_Key END)         AS Key1,
    MAX(CASE WHEN col_pos = 1 THEN Category_Key END)         AS Key2,
    MAX(CASE WHEN col_pos = 2 THEN Category_Key END)         AS Key3,

    MAX(CASE WHEN col_pos = 0 THEN Category_Name END)        AS Col1,
    MAX(CASE WHEN col_pos = 1 THEN Category_Name END)        AS Col2,
    MAX(CASE WHEN col_pos = 2 THEN Category_Name END)        AS Col3,

    MAX(CASE WHEN col_pos = 0 THEN Category_Description END) AS Des1,
    MAX(CASE WHEN col_pos = 1 THEN Category_Description END) AS Des2,
    MAX(CASE WHEN col_pos = 2 THEN Category_Description END) AS Des3
FROM Grouped
GROUP BY group_id
ORDER BY group_id;";
        }

        // ---------------------------- Exists ---------------------------------

        public static bool DoesCategoryExist(string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName))
                return false;

            var existsSql = LoadSqlRequired("s_Category_Exists.sql");

            using var conn = DatabaseHelper.GetAppOpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = existsSql;
            cmd.Parameters.AddWithValue("@CategoryName", categoryName.Trim());

            object? scalar = cmd.ExecuteScalar();
            return scalar != null && Convert.ToInt64(scalar) != 0;
        }

        public static bool DoesCategoryExistExceptKey(string categoryName, int categoryKey)
        {
            if (string.IsNullOrWhiteSpace(categoryName) || categoryKey <= 0)
                return false;

            var existsSql = LoadSqlRequired(Sql_CategoryExistsExceptId);

            using var conn = DatabaseHelper.GetAppOpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = existsSql;
            cmd.Parameters.AddWithValue("@CategoryName", categoryName.Trim());
            cmd.Parameters.AddWithValue("@CategoryKey", categoryKey);

            object? scalar = cmd.ExecuteScalar();
            return scalar != null && Convert.ToInt64(scalar) != 0;
        }

        // ---------------------------- Insert ---------------------------------

        /// <summary>
        /// Primary insert used by the inline Add Category UI.
        /// Category_Type is now a dumb column defaulted to 0 in the DB, so no type is required.
        /// </summary>
        public static void InsertCategory(string newCategory, string? newDescription)
            => InsertCategoryCore(newCategory, newDescription);

        /// <summary>
        /// Legacy overload that used to accept a category type description.
        /// Kept for compatibility; the type argument is now ignored.
        /// </summary>
        public static void InsertCategory(string newCategory, string? newDescription, string? categoryTypeDescription)
            => InsertCategoryCore(newCategory, newDescription);

        private static void InsertCategoryCore(string newCategory, string? newDescription)
        {
            if (newCategory is null) throw new ArgumentNullException(nameof(newCategory));

            // Validate name with central limits (min 4, max 64)
            var check = InputGuards.ValidateCategoryName(
                newCategory,
                minLen: MinCategoryNameLength,
                maxLen: MaxCategoryNameLength
            );

            if (!check.IsValid)
                throw new ArgumentException(
                    check.Error ?? $"Category name must be {MaxCategoryNameLength} characters or fewer.",
                    nameof(newCategory));

            string cleanName = check.CleanName;

            // Normalize description and fall back to the cleaned name if blank
            string? desc = InputGuards.NormalizeFreeText(newDescription, maxLen: MaxDescriptionLength);
            if (string.IsNullOrWhiteSpace(desc)) desc = cleanName;

            // Duplicate check
            if (DoesCategoryExist(cleanName))
            {
                // Best-effort: do nothing else; caller handles UX message.
                return;
            }

            var insertSql = LoadSqlRequired("s_Category_Insert.sql");

            using var conn = DatabaseHelper.GetAppOpenConnection();
            using var tx = conn.BeginTransaction();

            long newId;

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = insertSql;

                cmd.Parameters.AddWithValue("@CategoryName", cleanName);
                cmd.Parameters.AddWithValue("@Description", desc);

                int affected = cmd.ExecuteNonQuery();
                if (affected == 0)
                    throw new InvalidOperationException("Insert failed (no rows affected).");
            }

            using (var last = conn.CreateCommand())
            {
                last.Transaction = tx;
                last.CommandText = "SELECT last_insert_rowid();";
                newId = Convert.ToInt64(last.ExecuteScalar());
            }

            tx.Commit();

            VaultSessionStateService.MarkChanged();

            if (newId > 0 && newId <= int.MaxValue)
                CategorySessionStateService.RememberCreatedCategory((int)newId);

            // ------------------------------------------------------------
            // Template-based log (best-effort; must NOT block insert)
            // Uses common helper: TemplateLogWriter
            // ------------------------------------------------------------
            try
            {
                var tokens = new System.Collections.Generic.Dictionary<string, string?>
                {
                    ["CategoryName"] = cleanName
                };

                var write = new TemplateLogWriter.WriteRequest
                {
                    Level = "INFO",
                    Source = "Category",
                    EventCode = LogEventCategoryCreated,
                    ItemId = newId,
                    SubjectText = cleanName,
                    KeySetVersion = 1
                };

                long logId = TemplateLogWriter.InsertFromTemplates_BestEffort(
                    updateForm: CategoryLogUpdateForm,
                    seqsInOrder: new[] { 1 },
                    tokens: tokens,
                    write: write);

                // Fallback if no template rows exist yet
                if (logId <= 0)
                {
                    write.MessageText = $"Category {cleanName} has been created.";
                    TemplateLogWriter.InsertRendered_BestEffort(write);
                }
            }
            catch
            {
                // DO NOT BLOCK insert because of logging.
            }
        }

        // ---------------------------- Update ---------------------------------

        public static int UpdateCategory(int categoryKey, string categoryName, string? description)
        {
            var detail = LoadCategoryByKey(categoryKey);
            bool isActive = detail?.IsActive ?? true;
            return SaveCategoryEdit(categoryKey, categoryName, description, isActive);
        }

        public static int SaveCategoryEdit(int categoryKey, string categoryName, string? description, bool isActive)
        {
            if (categoryKey <= 0)
                throw new ArgumentOutOfRangeException(nameof(categoryKey), "Category key is required.");
            if (categoryName is null)
                throw new ArgumentNullException(nameof(categoryName));

            var check = InputGuards.ValidateCategoryName(
                categoryName,
                minLen: MinCategoryNameLength,
                maxLen: MaxCategoryNameLength
            );

            if (!check.IsValid)
                throw new ArgumentException(
                    check.Error ?? $"Category name must be {MaxCategoryNameLength} characters or fewer.",
                    nameof(categoryName));

            string cleanName = check.CleanName;

            string? desc = InputGuards.NormalizeFreeText(description, maxLen: MaxDescriptionLength);
            if (string.IsNullOrWhiteSpace(desc)) desc = cleanName;

            if (DoesCategoryExistExceptKey(cleanName, categoryKey))
                throw new InvalidOperationException("Category already exists. Please enter a different name.");

            var before = LoadCategoryByKey(categoryKey);
            if (before == null)
                return 0;

            if (before.IsActive && !isActive && CountActiveCategoryItemsByCategory(categoryKey) > 0)
                throw new InvalidOperationException("Category contains active items. Deactivate or move the items first.");

            var updateSql = LoadSqlRequired(Sql_CategoryUpdate);

            using var conn = DatabaseHelper.GetAppOpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = updateSql;
            cmd.Parameters.AddWithValue("@CategoryKey", categoryKey);
            cmd.Parameters.AddWithValue("@CategoryName", cleanName);
            cmd.Parameters.AddWithValue("@Description", desc);
            cmd.Parameters.AddWithValue("@IsActive", isActive ? 1 : 0);

            int affected = cmd.ExecuteNonQuery();
            if (affected <= 0)
                return affected;

            if (before.IsActive && !isActive)
                CategorySessionStateService.ForgetCategory(categoryKey);

            string? changeSummary = BuildCategoryChangeSummary(before, cleanName, desc, isActive);
            if (!string.IsNullOrWhiteSpace(changeSummary))
            {
                VaultSessionStateService.MarkChanged();
                LogCategoryUpdated_BestEffort(categoryKey, cleanName, changeSummary);
            }

            return affected;
        }

        private static string? BuildCategoryChangeSummary(
            CategoryDetail before,
            string afterName,
            string? afterDescription,
            bool afterIsActive)
        {
            var changes = new List<string>(capacity: 3);

            if (!TextEquals(before.Name, afterName))
                changes.Add($"Name changed from {N(before.Name)} to {N(afterName)}");

            if (!TextEquals(before.Description, afterDescription))
                changes.Add("Description changed");

            if (before.IsActive != afterIsActive)
            {
                string beforeStatus = FormatActiveStatus(before.IsActive);
                string afterStatus = FormatActiveStatus(afterIsActive);
                changes.Add($"Status changed from {beforeStatus} to {afterStatus}");
            }

            return changes.Count == 0
                ? null
                : string.Join("; ", changes);
        }

        private static string FormatActiveStatus(bool isActive)
            => isActive ? "Active" : "Inactive";

        public static int DeactivateCategory(int categoryKey)
        {
            if (categoryKey <= 0)
                throw new ArgumentOutOfRangeException(nameof(categoryKey), "Category key is required.");

            var detail = LoadCategoryByKey(categoryKey);
            if (detail == null)
                return 0;

            return SaveCategoryEdit(categoryKey, detail.Name, detail.Description, false);
        }

        private static void LogCategoryUpdated_BestEffort(int categoryKey, string categoryName, string changeSummary)
        {
            try
            {
                var tokens = new Dictionary<string, string?>
                {
                    ["CategoryName"] = categoryName,
                    ["ChangeSummary"] = changeSummary
                };

                var write = new TemplateLogWriter.WriteRequest
                {
                    Level = "INFO",
                    Source = "Category",
                    EventCode = LogEventCategoryUpdated,
                    ItemId = categoryKey,
                    SubjectText = categoryName,
                    KeySetVersion = 1
                };

                long logId = TemplateLogWriter.InsertFromTemplates_BestEffort(
                    updateForm: CategoryLogUpdateForm,
                    seqsInOrder: new[] { 2 },
                    tokens: tokens,
                    write: write);

                if (logId <= 0)
                {
                    write.MessageText = $"Category {categoryName} was updated: {changeSummary}.";
                    TemplateLogWriter.InsertRendered_BestEffort(write);
                }
            }
            catch
            {
            }
        }

        private static string N(string? value) => (value ?? string.Empty).Trim();

        private static bool TextEquals(string? a, string? b)
            => string.Equals(N(a), N(b), StringComparison.Ordinal);

        // ------------------------- Internal utils ----------------------------

        private static int SafeGetOrdinal(SqliteDataReader r, string name)
        {
            try { return r.GetOrdinal(name); } catch { return -1; }
        }

        private static int? ReadNullableInt(SqliteDataReader r, int ordinal)
        {
            if (ordinal < 0 || r.IsDBNull(ordinal)) return null;
            try
            {
                return r.GetFieldType(ordinal) == typeof(int)
                    ? r.GetInt32(ordinal)
                    : Convert.ToInt32(r.GetValue(ordinal));
            }
            catch
            {
                try { return Convert.ToInt32(r.GetInt64(ordinal)); }
                catch { return null; }
            }
        }

        private static string? ReadNullableString(SqliteDataReader r, int ordinal)
        {
            if (ordinal < 0 || r.IsDBNull(ordinal)) return "";
            try { return r.GetString(ordinal); } catch { return r.GetValue(ordinal)?.ToString() ?? ""; }
        }

        private static bool? ReadNullableBoolDefaultTrue(SqliteDataReader r, int ordinal)
        {
            if (ordinal < 0 || r.IsDBNull(ordinal)) return true;
            return (ReadNullableInt(r, ordinal) ?? 1) != 0;
        }
    }
}
