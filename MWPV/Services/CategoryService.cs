using Microsoft.Data.Sqlite;
using MWPV.Models;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Utilities.Helpers;   // DatabaseHelper, ErrorHandler
using Utilities.Sql;      // SqlCagegory
using Security.Utility;   // InputGuards
using MWPV.Services;      // LogCatalogService

namespace MWPV.Services
{
    public static class CategoryService
    {
        // Small DTO for combo binding
        public sealed class CategoryTypeOption
        {
            public string Code { get; init; } = "";
            public string Description { get; init; } = "";
        }

        // --------------------------------------------------------------------
        // Helpers
        // --------------------------------------------------------------------

        private static string LoadSqlRequired(string assetName)
        {
            var sql = SqlCagegory.GetSql(assetName);
            if (string.IsNullOrWhiteSpace(sql))
                throw new InvalidOperationException($"SQL not loaded: {assetName}");
            return sql;
        }

        private static string? TryLoadSql(string assetName)
        {
            try
            {
                var sql = SqlCagegory.GetSql(assetName);
                return string.IsNullOrWhiteSpace(sql) ? null : sql;
            }
            catch
            {
                return null;
            }
        }

        // --------------------------------------------------------------------
        // Reads
        // --------------------------------------------------------------------

        public static ObservableCollection<Categories> LoadCategories()
        {
            var rows = new ObservableCollection<Categories>();

            try
            {
                var selectSql = LoadSqlRequired("SelectCategories.sql");

                using var conn = DatabaseHelper.GetAppOpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = selectSql;

                using var r = cmd.ExecuteReader();

                int iCol1 = r.GetOrdinal("Col1");
                int iCol2 = r.GetOrdinal("Col2");
                int iCol3 = r.GetOrdinal("Col3");
                int iDes1 = r.GetOrdinal("Des1");
                int iDes2 = r.GetOrdinal("Des2");
                int iDes3 = r.GetOrdinal("Des3");

                while (r.Read())
                {
                    rows.Add(new Categories
                    {
                        strCategory1 = r.IsDBNull(iCol1) ? "" : r.GetString(iCol1),
                        strCategory2 = r.IsDBNull(iCol2) ? "" : r.GetString(iCol2),
                        strCategory3 = r.IsDBNull(iCol3) ? "" : r.GetString(iCol3),
                        strCategoryToolTip1 = r.IsDBNull(iDes1) ? "" : r.GetString(iDes1),
                        strCategoryToolTip2 = r.IsDBNull(iDes2) ? "" : r.GetString(iDes2),
                        strCategoryToolTip3 = r.IsDBNull(iDes3) ? "" : r.GetString(iDes3),
                    });
                }

#if DEBUG
                try
                {
                    Debug.WriteLine($"[CATEGORIES][LOAD] rows={rows.Count}");
                    int idx = 0;
                    foreach (var c in rows)
                        Debug.WriteLine($"[CATEGORIES][{idx++}] '{c.strCategory1}' | '{c.strCategory2}' | '{c.strCategory3}'");
                }
                catch { /* ignore debug write issues */ }
#endif
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error loading categories");
            }

            return rows;
        }

        /// <summary>
        /// Load list of category types for combo binding.
        /// Expects SQL to return columns: Code, Description.
        /// </summary>
        public static ObservableCollection<CategoryTypeOption> LoadCategoryTypes()
        {
            var rows = new ObservableCollection<CategoryTypeOption>();
            try
            {
                // Prefer specific name; allow alternate in case a different asset id is used.
                var sql = TryLoadSql("SelectCategoryTypes.sql")
                          ?? TryLoadSql("Select_Combo_CategoryTypes.sql")
                          ?? LoadSqlRequired("SelectCategoryTypes.sql"); // throws if none found

                using var conn = DatabaseHelper.GetAppOpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;

                using var r = cmd.ExecuteReader();
                int iCode = r.GetOrdinal("Code");
                int iDesc = r.GetOrdinal("Description");

                while (r.Read())
                {
                    rows.Add(new CategoryTypeOption
                    {
                        Code = r.IsDBNull(iCode) ? "" : r.GetString(iCode),
                        Description = r.IsDBNull(iDesc) ? "" : r.GetString(iDesc),
                    });
                }

#if DEBUG
                try { Debug.WriteLine($"[CAT_TYPES][LOAD] rows={rows.Count}"); } catch { }
#endif
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error loading category types");
            }

            return rows;
        }

        // --------------------------------------------------------------------
        // Exists
        // --------------------------------------------------------------------

        public static bool DoesCategoryExist(string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName))
                return false;

            var existsSql = LoadSqlRequired("CategoryExists.sql");

            using var conn = DatabaseHelper.GetAppOpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = existsSql;

            // Forward-only: one canonical param name
            cmd.Parameters.AddWithValue("@CategoryName", categoryName.Trim());

            object? scalar = cmd.ExecuteScalar();
            return scalar != null && Convert.ToInt64(scalar) != 0;
        }

        // --------------------------------------------------------------------
        // Insert (idempotent-at-call-site; warns on dup)
        // --------------------------------------------------------------------

        /// <summary>
        /// Insert if not present; on success logs INFO with { categoryId, categoryName }.
        /// On duplicate, logs WARN with { categoryName }.
        /// </summary>
        public static void InsertCategory(string newCategory, string? newDescription)
        {
            InsertCategoryCore(newCategory, newDescription, categoryTypeCode: null);
        }

        /// <summary>
        /// Overload to support a category type code from the Add Category UI.
        /// Matches InsertCategory.sql which expects @TypeCode (TEXT),
        /// resolves it to ComboDetailId inside SQL, and sets CreatedUtc via strftime(...).
        /// </summary>
        public static void InsertCategory(string newCategory, string? newDescription, string? categoryTypeCode)
        {
            InsertCategoryCore(newCategory, newDescription, categoryTypeCode);
        }

        private static void InsertCategoryCore(string newCategory, string? newDescription, string? categoryTypeCode)
        {
            if (newCategory is null) throw new ArgumentNullException(nameof(newCategory));

            var check = InputGuards.ValidateCategoryName(newCategory, minLen: 4, maxLen: 17);
            if (!check.IsValid)
                throw new ArgumentException(check.Error ?? "Invalid category name.", nameof(newCategory));

            string cleanName = check.CleanName;
            string? desc = Security.Utility.InputGuards.NormalizeFreeText(newDescription, maxLen: 512);
            if (string.IsNullOrWhiteSpace(desc)) desc = cleanName;

            // Type code from the combo (not sensitive; trim only). Required by SQL.
            string? typeCode = string.IsNullOrWhiteSpace(categoryTypeCode) ? null : categoryTypeCode.Trim();
            if (string.IsNullOrWhiteSpace(typeCode))
                throw new ArgumentException("Category type is required.", nameof(categoryTypeCode));

            if (DoesCategoryExist(cleanName))
            {
                // WARN duplicate
                try
                {
                    LogCatalogService.AppendJson(
                        level: "WARN",
                        source: "CategoryService",
                        eventCode: "CATEGORY_DUPLICATE",
                        dto: new
                        {
                            message = $"Duplicate category '{cleanName}' detected; insert skipped",
                            occurredUtc = DateTime.UtcNow,
                            categoryName = cleanName
                        }
                    );
                }
                catch { /* logging must never throw */ }
                return;
            }

            // Use the single canonical insert that expects @TypeCode (TEXT).
            var insertSql = LoadSqlRequired("InsertCategory.sql");

            using var conn = DatabaseHelper.GetAppOpenConnection();
            using var tx = conn.BeginTransaction();

            long newId;

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = insertSql;

                // Param names must match InsertCategory.sql
                cmd.Parameters.AddWithValue("@CategoryName", cleanName);
                cmd.Parameters.AddWithValue("@Description", desc);
                cmd.Parameters.AddWithValue("@TypeCode", typeCode);

                // Do NOT set @CreatedUtc here; the SQL sets it with strftime(...)
                int affected = cmd.ExecuteNonQuery();
                if (affected == 0)
                    throw new InvalidOperationException("Insert failed (no rows affected). Check @TypeCode mapping.");
            }

            using (var last = conn.CreateCommand())
            {
                last.Transaction = tx;
                last.CommandText = "SELECT last_insert_rowid();";
                newId = Convert.ToInt64(last.ExecuteScalar());
            }

            tx.Commit();

            // INFO inserted
            try
            {
                LogCatalogService.AppendJson(
                    level: "INFO",
                    source: "CategoryService",
                    eventCode: "CATEGORY_INSERTED",
                    dto: new
                    {
                        message = $"Category '{cleanName}' inserted",
                        occurredUtc = DateTime.UtcNow,
                        categoryId = newId,
                        categoryName = cleanName,
                        typeCode = typeCode
                    }
                );
            }
            catch { /* logging must never throw */ }
        }
    }
}
