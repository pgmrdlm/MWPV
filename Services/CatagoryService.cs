using Microsoft.Data.Sqlite;
using MWPV.Models;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using Utilities.Helpers;
using Utilities.Security;

namespace MWPV.Services
{
    public static class CategoryService
    {
        //private static readonly string sourceDir = "C:\\Users\\pgmrd\\My Drive\\MWPV\\MWPV\\sql";
        //private static readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MWPV", "sql");
        //`private static readonly string _connectionString;
        //private static readonly string _sqlQuery;
        //private static readonly string _sqlInsert;
        //private static readonly string _sqlCatagoryExists;

        //static CategoryService()


        public static ObservableCollection<Catagories> LoadCatagories()
        {
            var catagories = new ObservableCollection<Catagories>();
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
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    stage = "materialize-row";
                    catagories.Add(new Catagories
                    {
                        strCategory1 = reader["Col1"] is not DBNull ? reader["Col1"].ToString() : "",
                        strCategory2 = reader["Col2"] is not DBNull ? reader["Col2"].ToString() : "",
                        strCategory3 = reader["Col3"] is not DBNull ? reader["Col3"].ToString() : "",
                        strCategoryToolTip1 = reader["Des1"] is not DBNull ? reader["Des1"].ToString() : "",
                        strCategoryToolTip2 = reader["Des2"] is not DBNull ? reader["Des2"].ToString() : "",
                        strCategoryToolTip3 = reader["Des3"] is not DBNull ? reader["Des3"].ToString() : ""
                    });
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error during database initialization", stage: stage);
            }

            return catagories;
        }



        public static void InsertCategory(string newCategory, string newDescription)
        {
            if (newCategory == null)
                throw new ArgumentNullException(nameof(newCategory));

            // ---- Normalize inputs ----
            string name = newCategory.Trim();

            if (name.Length < 4)
                throw new ArgumentException("Category name must be at least 4 characters.", nameof(newCategory));
            if (name.Length > 17)
                throw new ArgumentException("Category name must be 17 characters or fewer.", nameof(newCategory));

            // Strip control chars (except CR/LF/TAB)
            name = Regex.Replace(name, @"[\p{C}&&[^\r\n\t]]", string.Empty);

            string desc = (newDescription ?? string.Empty).Trim();
            if (desc.Length > 512) desc = desc.Substring(0, 512);
            desc = Regex.Replace(desc, @"[\p{C}&&[^\r\n\t]]", string.Empty);

            // Ensure non-empty description
            if (string.IsNullOrWhiteSpace(desc))
                desc = name;

            // ---- Load SQL ----
            string insertSql = SecureEncryptedDataStore.GetString("InsertCatagory.sql");
            if (string.IsNullOrWhiteSpace(insertSql))
                throw new InvalidOperationException("InsertCatagory.sql was not found or is empty.");

            // ---- Execute ----
            using var conn = DatabaseHelper.GetAppOpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = insertSql;

            cmd.Parameters.AddWithValue("@catagoryname", name);
            cmd.Parameters.AddWithValue("@description", desc);

            cmd.ExecuteNonQuery();
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

            var scalar = cmd.ExecuteScalar();           // EXISTS -> boxed long 0/1
            return scalar != null && Convert.ToInt64(scalar) == 1;
        }


    }
}
