using System;
using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Utilities.Helpers;          // DatabaseHelper
using MWPV.Services;              // LogCatalogService

namespace MWPV.Utilities.Diagnostics
{
    public static class SmokeTester
    {
        public static void Run()
        {
            // ---- READ: count categories (correct table name) ----
            try
            {
                using var cn = DatabaseHelper.GetAppOpenConnection();
                using var cmd = cn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM Category;"; // <— FIXED: singular
                var cnt = Convert.ToInt32(cmd.ExecuteScalar());
                Debug.WriteLine($"[SMOKE][READ] Category rows={cnt}");
            }
            catch (SqliteException ex)
            {
                Debug.WriteLine($"[SMOKE][READ][FAIL] {ex.GetType().Name}: {ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SMOKE][READ][FAIL] {ex.GetType().Name}: {ex.Message}");
            }

            // ---- WRITE: log a login event (unchanged) ----
            try
            {
                var id = LogCatalogService.InsertLoginEvent();
                Debug.WriteLine($"[SMOKE][WRITE] InsertLoginEvent id={id}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SMOKE][WRITE][FAIL] {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
