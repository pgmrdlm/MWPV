using Microsoft.Data.Sqlite;
using MWPV.Models;
using System;
using System.Collections.ObjectModel;
using Utilities.Helpers;    // DatabaseHelper, ErrorHandler
using Security.Utility;   // SecureEncryptedDataStore, SensitiveDataCleaner, InputGuards, SecureLogService
using Utilities.Logging;    // LogSeverity, LogEventIds

namespace MWPV.Services
{
    public static class CategoryService
    {
        /// <summary>
        /// Loads categories using SelectCatagories.sql (encrypted asset).
        /// </summary>
        public static ObservableCollection<Catagories> LoadCatagories()
        {
            var rows = new ObservableCollection<Catagories>();
            string stage = "init";

            try
            {
                stage = "load-sql";
                string selectSql = SecureEncryptedDataStore.GetString("SelectCatagories.sql");

                stage = "open-conn";
                using var conn = DatabaseHelper.GetAppOpenConnection();

                stage = "prep-cmd";
                using var cmd = conn.CreateCommand();
                cmd.CommandText = selectSql;

                stage = "exec-reader";
                using var r = cmd.ExecuteReader();

                // Cache ordinals once
                int iCol1 = r.GetOrdinal("Col1");
                int iCol2 = r.GetOrdinal("Col2");
                int iCol3 = r.GetOrdinal("Col3");
                int iDes1 = r.GetOrdinal("Des1");
                int iDes2 = r.GetOrdinal("Des2");
                int iDes3 = r.GetOrdinal("Des3");

                while (r.Read())
                {
                    rows.Add(new Catagories
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

        /// <summary>
        /// Inserts a new category if it doesn't already exist.
        /// - Validates/normalizes name & description.
        /// - Duplicate → warn log and no-op.
        /// </summary>
        public static void InsertCategory(string newCategory, string? newDescription)
        {
            if (newCategory is null)
                throw new ArgumentNullException(nameof(newCategory));

            // Validate / normalize
            var check = InputGuards.ValidateCategoryName(newCategory, minLen: 4, maxLen: 17);
            if (!check.IsValid)
                throw new ArgumentException(check.Error ?? "Invalid category name.", nameof(newCategory));

            string cleanName = check.CleanName;
            string? desc = InputGuards.NormalizeFreeText(newDescription, maxLen: 512);
            if (string.IsNullOrWhiteSpace(desc))
                desc = cleanName; // never store empty tooltip

            // Duplicate guard
            if (DoesCatagoryExist(cleanName))
            {
                _ = SecureLogService.WriteAsync(
                    level: LogSeverity.Warn,
                    payload: new { name = cleanName, reason = "duplicate" },
                    eventCode: "CATEGORY_DUPLICATE",
                    source: "CategoryService.InsertCategory"
                );
                return;
            }

            string insertSql = SecureEncryptedDataStore.GetString("InsertCatagory.sql");
            if (string.IsNullOrWhiteSpace(insertSql))
                throw new InvalidOperationException("InsertCatagory.sql not found or empty.");

            using var conn = DatabaseHelper.GetAppOpenConnection();
            using var tx = conn.BeginTransaction();

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = insertSql;
            cmd.Parameters.AddWithValue("@catagoryname", cleanName);
            cmd.Parameters.AddWithValue("@description", desc);

            cmd.ExecuteNonQuery();
            tx.Commit();

            // (Optional) info log on success (kept quiet to reduce noise)
            //_ = SecureLogService.WriteAsync(LogSeverity.Info, new { name = cleanName }, "CATEGORY_ADDED", "CategoryService.InsertCategory");
        }

        /// <summary>
        /// Checks existence using CatagoryExists.sql. Returns true if exists.
        /// </summary>
        public static bool DoesCatagoryExist(string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName))
                return false;

            string sql = SecureEncryptedDataStore.GetString("CatagoryExists.sql");

            using var conn = DatabaseHelper.GetAppOpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@catagoryname", categoryName.Trim());

            object? scalar = cmd.ExecuteScalar(); // expect 0 or 1
            return scalar != null && Convert.ToInt64(scalar) == 1;
        }
    }
}
