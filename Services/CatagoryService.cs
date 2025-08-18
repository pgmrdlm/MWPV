using Microsoft.Data.Sqlite;
using MWPV.Models;
using System;
using System.Collections.ObjectModel;
using Utilities.Helpers;   // DatabaseHelper, ErrorHandler
using Utilities.Security;  // InputGuards

namespace MWPV.Services
{
    public static class CategoryService
    {
        public static ObservableCollection<Catagories> LoadCatagories()
        {
            var rows = new ObservableCollection<Catagories>();
            string stage = "init";

            try
            {
                stage = "get-sql";
                string selectSql = SecureEncryptedDataStore.GetString("SelectCatagories.sql");

                stage = "open-connection";
                using var conn = DatabaseHelper.GetAppOpenConnection();

                stage = "prepare-command";
                using var cmd = conn.CreateCommand();
                cmd.CommandText = selectSql;

                stage = "execute-reader";
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
                    stage = "materialize-row";
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
                ErrorHandler.Abend(ex, "Error during database initialization", stage: stage);
            }

            return rows;
        }

        public static void InsertCategory(string newCategory, string? newDescription)
        {
            if (newCategory is null)
                throw new ArgumentNullException(nameof(newCategory));

            // Defensive re-validation (centralized rules)
            var check = InputGuards.ValidateCategoryName(newCategory, 4, 17);
            if (!check.IsValid)
                throw new ArgumentException(check.Error ?? "Invalid category name.", nameof(newCategory));

            // Normalize free text (caps length, strips control chars, forbids <, >, | by default)
            string? desc = InputGuards.NormalizeFreeText(newDescription, 512);
            if (string.IsNullOrWhiteSpace(desc))
                desc = check.CleanName; // never store empty tooltip

            string insertSql = SecureEncryptedDataStore.GetString("InsertCatagory.sql");
            if (string.IsNullOrWhiteSpace(insertSql))
                throw new InvalidOperationException("InsertCatagory.sql was not found or is empty.");

            using var conn = DatabaseHelper.GetAppOpenConnection();
            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();

            cmd.Transaction = tx;
            cmd.CommandText = insertSql;
            cmd.Parameters.AddWithValue("@catagoryname", check.CleanName);
            cmd.Parameters.AddWithValue("@description", desc);

            cmd.ExecuteNonQuery();
            tx.Commit();
        }

        public static bool DoesCatagoryExist(string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName))
                return false;

            string sql = SecureEncryptedDataStore.GetString("CatagoryExists.sql");

            using var conn = DatabaseHelper.GetAppOpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@catagoryname", categoryName.Trim());

            object? scalar = cmd.ExecuteScalar(); // EXPECT: 0/1
            return scalar != null && Convert.ToInt64(scalar) == 1;
        }
    }
}
