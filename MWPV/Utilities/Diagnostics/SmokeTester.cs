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
                // (A) Optional read test (keep or drop as you wish)
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

                // (B) Write a login heartbeat row via the SERVICE (single source of truth)
                var id = LogCatalogService.InsertLoginEvent(DatabaseHelper.GetAppOpenConnection);
                if (id > 0)
                    Debug.WriteLine($"[SMOKE][WRITE] InsertLoginEvent id={id}");
                else
                    Debug.WriteLine("[SMOKE][WRITE][FAIL] InsertLoginEvent returned -1");

                // No delete. Row is kept as a login heartbeat.
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SMOKE][FAIL] {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
