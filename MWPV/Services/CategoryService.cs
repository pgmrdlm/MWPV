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
        // DTO for combo binding
        public sealed class CategoryTypeOption
        {
            public string Code { get; init; } = "";
            public string Description { get; init; } = "";
        }

        // --------------------------- Helpers ---------------------------------

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
            catch { return null; }
        }

        // ---------------------------- Reads ----------------------------------

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
                catch { }
#endif
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error loading categories");
            }

            return rows;
        }

        /// <summary>
        /// Load list of category types for combo binding (expects columns: Code, Description).
        /// </summary>
        public static ObservableCollection<CategoryTypeOption> LoadCategoryTypes()
        {
            var rows = new ObservableCollection<CategoryTypeOption>();
            try
            {
                var sql = TryLoadSql("s_Combo_CategoryType.sql")
                          ?? TryLoadSql("Select_Combo_CategoryTypes.sql")
                          ?? LoadSqlRequired("s_Combo_CategoryType.sql");

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

        public static void InsertCategory(string newCategory, string? newDescription)
            => InsertCategoryCore(newCategory, newDescription, typeDescriptionFromUi: null);

        /// <summary>
        /// Insert using the combo's displayed Description text.
        /// SQL resolves the Description to the proper ComboDetail row.
        /// </summary>
        public static void InsertCategory(string newCategory, string? newDescription, string? categoryTypeDescription)
            => InsertCategoryCore(newCategory, newDescription, categoryTypeDescription);

        private static void InsertCategoryCore(string newCategory, string? newDescription, string? typeDescriptionFromUi)
        {
            if (newCategory is null) throw new ArgumentNullException(nameof(newCategory));

            var check = InputGuards.ValidateCategoryName(newCategory, minLen: 4, maxLen: 17);
            if (!check.IsValid)
                throw new ArgumentException(check.Error ?? "Invalid category name.", nameof(newCategory));

            string cleanName = check.CleanName;
            string? desc = InputGuards.NormalizeFreeText(newDescription, maxLen: 512);
            if (string.IsNullOrWhiteSpace(desc)) desc = cleanName;

            string? typeDescription = string.IsNullOrWhiteSpace(typeDescriptionFromUi) ? null : typeDescriptionFromUi.Trim();
            if (string.IsNullOrWhiteSpace(typeDescription))
                throw new ArgumentException("Category type is required.", nameof(typeDescriptionFromUi));

            if (DoesCategoryExist(cleanName))
            {
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
                        });
                }
                catch { }
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
                cmd.Parameters.AddWithValue("@TypeDescription", typeDescription);

#if DEBUG
                try { Debug.WriteLine($"[CAT][INSERT] name='{cleanName}' typeDesc='{typeDescription}'"); } catch { }
#endif
                int affected = cmd.ExecuteNonQuery();
                if (affected == 0)
                    throw new InvalidOperationException("Insert failed (no rows affected). Check @TypeDescription mapping to ComboDetail.");
            }

            using (var last = conn.CreateCommand())
            {
                last.Transaction = tx;
                last.CommandText = "SELECT last_insert_rowid();";
                newId = Convert.ToInt64(last.ExecuteScalar());
            }

            tx.Commit();

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
                        typeDescription = typeDescription
                    });
            }
            catch { }
        }
    }
}
