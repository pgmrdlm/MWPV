// Utilities/Diagnostics/SmokeTester.cs
using System;
using System.Diagnostics;
using Utilities.Helpers;        // DatabaseHelper
using MWPV.Services;           // LogCatalogService

namespace Utilities.Diagnostics
{
    public static class SmokeTester
    {
        [Conditional("DEBUG")]
        public static void Run()
        {
            try
            {
                // Optional: quick read check (safe to keep for sanity)
                try
                {
                    using var cn = DatabaseHelper.OpenConnection();
                    using var cmd = cn.CreateCommand();
                    cmd.CommandText = "SELECT COUNT(*) FROM Categories;";
                    var cnt = Convert.ToInt64(cmd.ExecuteScalar());
                    Debug.WriteLine($"[SMOKE][READ] Categories rows={cnt}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SMOKE][READ][FAIL] {ex.GetType().Name}: {ex.Message}");
                }

                // Write a login heartbeat THROUGH THE SERVICE (centralized insert)
                // Service handles mapping to Logs_Insert_V3.sql
                long id = LogCatalogService.InsertLoginEvent(DatabaseHelper.GetAppOpenConnection);

                if (id > 0)
                    Debug.WriteLine($"[SMOKE][WRITE] InsertLoginEvent id={id}");
                else
                    Debug.WriteLine("[SMOKE][WRITE][FAIL] InsertLoginEvent returned -1");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SMOKE][FAIL] {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
