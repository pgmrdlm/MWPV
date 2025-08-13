// Utilities/Sql/SchemaBootstrap.cs
using System;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace Utilities.Sql
{
    /// <summary>
    /// Runs idempotent log schema steps (Init + Indexes).
    /// </summary>
    public static class SchemaBootstrap
    {
        /// <summary>
        /// Call this after the DB is open and after SqlCatagory.LoadAll() has loaded scripts into the secure store.
        /// </summary>
        public static void EnsureLogsSchema(SqliteConnection openConn)
        {
            if (openConn == null) throw new ArgumentNullException(nameof(openConn));

            // Scripts contain their own BEGIN/COMMIT; do NOT wrap in another transaction.
            ExecScriptTolerant(openConn, SecureSql.Require("Logs_Init.sql"));
            ExecScriptTolerant(openConn, SecureSql.Require("Logs_Indexes.sql"));
        }

        private static void ExecScriptTolerant(SqliteConnection conn, string script)
        {
            // Execute statements one-by-one so we can ignore harmless idempotency errors
            var statements = script.Split(';')
                                   .Select(s => s.Trim())
                                   .Where(s => s.Length > 0);

            foreach (var sql in statements)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                try
                {
                    cmd.ExecuteNonQuery();
                }
                catch (SqliteException ex)
                {
                    var msg = ex.Message?.ToLowerInvariant() ?? "";
                    // Ignore duplicates (e.g., ALTER ADD COLUMN on existing columns, CREATE ... IF NOT EXISTS)
                    if (msg.Contains("duplicate column name") || msg.Contains("already exists"))
                    {
                        System.Diagnostics.Debug.WriteLine($"[Bootstrap] Ignored: {ex.Message}");
                        continue;
                    }
                    throw; // real error
                }
            }
        }
    }
}
