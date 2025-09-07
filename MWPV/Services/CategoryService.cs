using Microsoft.Data.Sqlite;
using MWPV.Models;
using System;
using System.Collections.ObjectModel;
using Utilities.Helpers;     // DatabaseHelper, ErrorHandler
using Security.Utility;      // SecureEncryptedDataStore, InputGuards, SecureLogService
using LogSeverity = Security.Utility.Logging.LogSeverity;  // disambiguate enum

namespace MWPV.Services
{
    public static class CategoryService
    {
        // --------------------------------------------------------------------
        // Helpers
        // --------------------------------------------------------------------
        private static string? LoadSqlOrNull(params string[] names)
        {
            foreach (var name in names)
            {
                try
                {
                    var s = SecureEncryptedDataStore.GetString(name);
                    if (!string.IsNullOrWhiteSpace(s))
                        return s;
                }
                catch { /* asset not found in this catalog name; try next */ }
            }
            return null;
        }

        private static void AddParamAliases(SqliteCommand cmd, string[] names, object? value)
        {
            foreach (var n in names)
            {
                if (!cmd.Parameters.Contains(n))
                    cmd.Parameters.AddWithValue(n, value ?? DBNull.Value);
            }
        }

        // --------------------------------------------------------------------
        // LOAD
        // --------------------------------------------------------------------
        /// <summary>
        /// Loads categories from the encrypted SQL asset (new name first, legacy fallback).
        /// </summary>
        public static ObservableCollection<Categories> LoadCategories()
        {
            var rows = new ObservableCollection<Categories>();
            var stage = "init";

            try
            {
                stage = "load-sql";
                var selectSql = LoadSqlOrNull(
                    "SelectCategories.sql",       // canonical
                    "SelectCatagories.sql"        // legacy misspelling
                ) ?? throw new InvalidOperationException("SelectCategories.sql not found in the key archive.");

                stage = "open-conn";
                using var conn = DatabaseHelper.GetAppOpenConnection();

                stage = "prep";
                using var cmd = conn.CreateCommand();
                cmd.CommandText = selectSql;

                stage = "exec";
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
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error loading categories", stage: stage);
            }

            return rows;
        }

        // Legacy shim (kept for callers during transition)
        [Obsolete("Use LoadCategories()")]
        public static ObservableCollection<Categories> LoadCatagories() => LoadCategories();

        // --------------------------------------------------------------------
        // INSERT
        // --------------------------------------------------------------------
        /// <summary>
        /// Inserts a new category if it doesn't already exist.
        /// Validates and normalizes inputs; description defaults to name.
        /// </summary>
        public static void InsertCategory(string newCategory, string? newDescription)
        {
            if (newCategory is null) throw new ArgumentNullException(nameof(newCategory));

            var check = InputGuards.ValidateCategoryName(newCategory, minLen: 4, maxLen: 17);
            if (!check.IsValid)
                throw new ArgumentException(check.Error ?? "Invalid category name.", nameof(newCategory));

            string cleanName = check.CleanName;
            string? desc = InputGuards.NormalizeFreeText(newDescription, maxLen: 512);
            if (string.IsNullOrWhiteSpace(desc)) desc = cleanName;

            if (DoesCategoryExist(cleanName))
            {
                _ = SecureLogService.WriteAsync(
                    level: LogSeverity.Warn,
                    payload: new { name = cleanName, reason = "duplicate" },
                    eventCode: "CATEGORY_DUPLICATE",
                    source: "CategoryService.InsertCategory",
                    message: "Duplicate category detected; insert skipped");
                return;
            }

            var insertSql = LoadSqlOrNull(
                "InsertCategory.sql",   // canonical
                "InsertCagegory.sql"    // legacy misspelling
            ) ?? "INSERT INTO Category (Category_Name, Category_Description, IsActive) " +
                 "VALUES (@CategoryName, @Description, 1);"; // safe inline fallback

            using var conn = DatabaseHelper.GetAppOpenConnection();
            using var tx = conn.BeginTransaction();

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = insertSql;

            // Bind both modern and legacy parameter names
            AddParamAliases(cmd, new[] { "@CategoryName", "@Categoryname", "@Cagegoryname" }, cleanName);
            AddParamAliases(cmd, new[] { "@Description", "@description" }, desc!);

            cmd.ExecuteNonQuery();
            tx.Commit();
        }

        // --------------------------------------------------------------------
        // EXISTS
        // --------------------------------------------------------------------
        /// <summary>
        /// Returns true if a category with the given name exists (case-insensitive).
        /// </summary>
        public static bool DoesCategoryExist(string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName))
                return false;

            var existsSql = LoadSqlOrNull(
                "CategoryExists.sql",   // canonical
                "CagegoryExists.sql"    // legacy misspelling
            ) ?? "SELECT EXISTS ( " +
                 "  SELECT 1 FROM Category " +
                 "  WHERE Category_Name = @CategoryName COLLATE NOCASE LIMIT 1 " +
                 ");";

            using var conn = DatabaseHelper.GetAppOpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = existsSql;

            // Bind both modern and legacy parameter names
            AddParamAliases(cmd, new[] { "@CategoryName", "@Categoryname", "@Cagegoryname" }, categoryName.Trim());

            object? scalar = cmd.ExecuteScalar();
            return scalar != null && Convert.ToInt64(scalar) != 0;
        }

        // Legacy shim
        [Obsolete("Use DoesCategoryExist(name)")]
        public static bool DoesCatagoryExist(string categoryName) => DoesCategoryExist(categoryName);
    }
}
