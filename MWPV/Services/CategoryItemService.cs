using Microsoft.Data.Sqlite;
using MWPV.Models;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Utilities.Helpers;   // DatabaseHelper, ErrorHandler
using Utilities.Sql;      // SqlCategory
using Security.Utility;   // InputGuards
using MWPV.Services;      // LogCatalogService

namespace MWPV.Services
{
    public static class CategoryItemService
    {
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
        /// Load category items for a given category key.
        /// Maps directly to the CategoryItemGriud model.
        /// </summary>
        public static ObservableCollection<CategoryItemGriud> LoadCategoryItems(int categoryKey)
        {
            var rows = new ObservableCollection<CategoryItemGriud>();

            try
            {
                var selectSql = LoadSqlRequired("s_CategoryItem_SelectGrid.sql");

                using var conn = DatabaseHelper.GetAppOpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = selectSql;
                cmd.Parameters.AddWithValue("@Category_Key", categoryKey);

                using var r = cmd.ExecuteReader();

                int iKey1 = r.GetOrdinal("Key1");
                int iKey2 = r.GetOrdinal("Key2");
                int iKey3 = r.GetOrdinal("Key3");
                int iCol1 = r.GetOrdinal("Col1");
                int iCol2 = r.GetOrdinal("Col2");
                int iCol3 = r.GetOrdinal("Col3");
                int iDes1 = r.GetOrdinal("Des1");
                int iDes2 = r.GetOrdinal("Des2");
                int iDes3 = r.GetOrdinal("Des3");

                while (r.Read())
                {
                    rows.Add(new CategoryItemGriud
                    {
                        strCategoryItemKey1 = r.IsDBNull(iKey1) ? "" : r.GetValue(iKey1)?.ToString(),
                        strCategoryItemKey2 = r.IsDBNull(iKey2) ? "" : r.GetValue(iKey2)?.ToString(),
                        strCategoryItemKey3 = r.IsDBNull(iKey3) ? "" : r.GetValue(iKey3)?.ToString(),
                        strCategoryItem1 = r.IsDBNull(iCol1) ? "" : r.GetString(iCol1),
                        strCategoryItem2 = r.IsDBNull(iCol2) ? "" : r.GetString(iCol2),
                        strCategoryItem3 = r.IsDBNull(iCol3) ? "" : r.GetString(iCol3),
                        strCategoryItemToolTip1 = r.IsDBNull(iDes1) ? "" : r.GetString(iDes1),
                        strCategoryItemToolTip2 = r.IsDBNull(iDes2) ? "" : r.GetString(iDes2),
                        strCategoryItemToolTip3 = r.IsDBNull(iDes3) ? "" : r.GetString(iDes3),
                    });
                }

#if DEBUG
                try
                {
                    Debug.WriteLine($"[CAT_ITEMS][LOAD] rows={rows.Count} for CatKey={categoryKey}");
                    int idx = 0;
                    foreach (var c in rows)
                        Debug.WriteLine($"[CAT_ITEMS][{idx++}] '{c.strCategoryItem1}' | '{c.strCategoryItem2}' | '{c.strCategoryItem3}'");
                }
                catch { }
#endif
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error loading category items");
            }

            return rows;
        }
    }
}
