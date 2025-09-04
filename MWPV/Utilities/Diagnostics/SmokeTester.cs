using System;
using System.Data;
using System.Diagnostics;
using Utilities.Helpers;   // DatabaseHelper
using Utilities.Sql;       // SqlCagegory

namespace Utilities.Diagnostics
{
    public static class SmokeTester
    {
        [Conditional("DEBUG")]
        public static void Run()
        {
            try
            {
                using var conn = DatabaseHelper.OpenConnection();

                // 1) READ TEST: Categories via cached SQL
                using (var readCmd = conn.CreateCommand())
                {
                    readCmd.CommandText = SqlCagegory.GetSql("SelectCategories.sql");
                    using var rdr = readCmd.ExecuteReader();
                    int rows = 0;
                    while (rdr.Read()) rows++;
                    Debug.WriteLine($"[SMOKE][READ] Categories rows={rows}");
                }

                // 2) WRITE TEST: Insert a log row via cached SQL (bind @-style params)
                var nowUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                long lastId;

                using (var insCmd = conn.CreateCommand())
                {
                    insCmd.CommandText = SqlCagegory.GetSql("Logs_Insert_V2.sql");

                    insCmd.Parameters.AddWithValue("@WhenUtc", nowUtc);
                    insCmd.Parameters.AddWithValue("@CreatedUtc", nowUtc);
                    insCmd.Parameters.AddWithValue("@Level", "INFO");
                    insCmd.Parameters.AddWithValue("@Source", "SmokeTest");
                    insCmd.Parameters.AddWithValue("@EventCode", "BOOT");
                    insCmd.Parameters.AddWithValue("@SessionId", "");
                    insCmd.Parameters.AddWithValue("@MachineId", Environment.MachineName ?? "");
                    insCmd.Parameters.AddWithValue("@AppVersion", AppVersion());
                    insCmd.Parameters.AddWithValue("@IsCrash", 0);

                    var pPl = insCmd.CreateParameter();
                    pPl.ParameterName = "@Payload";
                    pPl.DbType = DbType.Binary;
                    pPl.Value = DBNull.Value;     // keep it NULL for smoke
                    insCmd.Parameters.Add(pPl);

                    var pFmt = insCmd.CreateParameter();
                    pFmt.ParameterName = "@PayloadFmt";
                    pFmt.DbType = DbType.String;
                    pFmt.Value = DBNull.Value;    // keep it NULL for smoke
                    insCmd.Parameters.Add(pFmt);

                    insCmd.Parameters.AddWithValue("@StackHash", "");

                    var affected = insCmd.ExecuteNonQuery();
                    Debug.WriteLine($"[SMOKE][WRITE] Logs inserted rows={affected}");
                }

                // 3) VERIFY INSERT: fetch last insert id
                using (var lastIdCmd = conn.CreateCommand())
                {
                    lastIdCmd.CommandText = SqlCagegory.GetSql("Logs_LastInsertId.sql");
                    lastId = Convert.ToInt64(lastIdCmd.ExecuteScalar());
                    Debug.WriteLine($"[SMOKE][VERIFY] Logs last inserted id={lastId}");
                }

#if DEBUG
                // 4) DEBUG cleanup: remove the inserted smoke row
                using (var delCmd = conn.CreateCommand())
                {
                    delCmd.CommandText = "DELETE FROM Logs WHERE Id = @id";
                    delCmd.Parameters.AddWithValue("@id", lastId);
                    var deleted = delCmd.ExecuteNonQuery();
                    Debug.WriteLine($"[SMOKE][CLEAN] Deleted={deleted} id={lastId}");
                }
#endif
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SMOKE][FAIL] {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static string AppVersion()
        {
            var v = System.Reflection.Assembly.GetEntryAssembly()?.GetName()?.Version;
            return v?.ToString() ?? "dev";
        }
    }
}
