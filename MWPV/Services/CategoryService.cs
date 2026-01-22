// File: MWPV/Services/CategoryService.cs
using Microsoft.Data.Sqlite;
using MWPV.Models;
using System;
using System.Collections.ObjectModel;
using Utilities.Helpers;   // DatabaseHelper, ErrorHandler
using Utilities.Sql;      // SqlCagegory  (intentional spelling to match existing)
using Security.Utility;   // InputGuards

namespace MWPV.Services
{
    public static class CategoryService
    {
        // ===== Centralized limits to keep UI/Backend consistent =====
        private const int MinCategoryNameLength = 4;
        private const int MaxCategoryNameLength = 64; // was 17
        private const int MaxDescriptionLength = 512;

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
        /// </summary>
        public static ObservableCollection<Categories> LoadCategories()
        {
            var rows = new ObservableCollection<Categories>();

            try
            {
                var selectSql = LoadSqlRequired("s_CategorySelectAll.sql");

                using var conn = DatabaseHelper.GetAppOpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = selectSql;

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

                // Prefer templates if present:
                // UpdateForm = "Category"
                // Seq 1 = e.g. "Category #CategoryName# has been created"
                var write = new TemplateLogWriter.WriteRequest
                {
                    Level = "INFO",
                    Source = "Category",
                    EventCode = "CATEGORY_CREATED",
                    ItemId = newId,
                    SubjectText = cleanName,
                    KeySetVersion = 1
                };

                long logId = TemplateLogWriter.InsertFromTemplates_BestEffort(
                    updateForm: "Category",
                    seqsInOrder: new[] { 1 },
                    tokens: tokens,
                    write: write);

                // Fallback if no template rows exist yet
                if (logId <= 0)
                {
                    write.MessageText = $"Category '{cleanName}' has been created.";
                    TemplateLogWriter.InsertRendered(write);
                }
            }
            catch
            {
                // DO NOT BLOCK insert because of logging.
            }
        }

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
    }
}
