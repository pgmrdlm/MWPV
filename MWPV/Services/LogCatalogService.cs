using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using MWPV.Models;
using Utilities.Sql;        // SqlCagegory.GetSql(...)
using Utilities.Helpers;    // DatabaseHelper (for default connection factory)

namespace MWPV.Services
{
    /// <summary>
    /// Loader + simple writer for the log catalog.
    /// All SQL is externalized via SqlCagegory.GetSql(...).
    /// </summary>
    public static class LogCatalogService
    {
        /// <summary>
        /// Insert a lightweight "login heartbeat" row (INFO/Login/LOGIN). No payload.
        /// Returns inserted Id, or -1 on failure.
        /// </summary>
        public static long InsertLoginEvent(Func<SqliteConnection>? openAppConnection = null)
        {
            openAppConnection ??= DatabaseHelper.GetAppOpenConnection;

            try
            {
                using var cn = openAppConnection();

                var nowIso = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

                using (var cmd = cn.CreateCommand())
                {
                    cmd.CommandText = SqlCagegory.GetSql("Logs_Insert_V2.sql");

                    cmd.Parameters.AddWithValue("@WhenUtc", nowIso);
                    cmd.Parameters.AddWithValue("@CreatedUtc", nowIso);
                    cmd.Parameters.AddWithValue("@Level", "INFO");
                    cmd.Parameters.AddWithValue("@Source", "Login");
                    cmd.Parameters.AddWithValue("@EventCode", "LOGIN");
                    cmd.Parameters.AddWithValue("@SessionId", "");
                    cmd.Parameters.AddWithValue("@MachineId", Environment.MachineName ?? "");
                    cmd.Parameters.AddWithValue("@AppVersion", AppVersion());
                    cmd.Parameters.AddWithValue("@IsCrash", 0);

                    var pPl = cmd.CreateParameter();
                    pPl.ParameterName = "@Payload";
                    pPl.Value = DBNull.Value; // no payload for heartbeat
                    cmd.Parameters.Add(pPl);

                    var pFmt = cmd.CreateParameter();
                    pFmt.ParameterName = "@PayloadFmt";
                    pFmt.Value = DBNull.Value;
                    cmd.Parameters.Add(pFmt);

                    cmd.Parameters.AddWithValue("@StackHash", "");

                    var affected = cmd.ExecuteNonQuery();
                    if (affected != 1)
                    {
                        Debug.WriteLine("[LOGS][InsertLoginEvent] affected != 1");
                        return -1;
                    }
                }

                using var last = cn.CreateCommand();
                last.CommandText = SqlCagegory.GetSql("Logs_LastInsertId.sql");
                var obj = last.ExecuteScalar();
                var id = Convert.ToInt64(obj);
                Debug.WriteLine($"[LOGS][InsertLoginEvent] lastId={id}");
                return id;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LOGS][InsertLoginEvent][FAIL] {ex.GetType().Name}: {ex.Message}");
                return -1;
            }
        }

        /// <summary>
        /// Load ALL logs (newest first) into a new collection.
        /// </summary>
        public static async Task<ObservableCollection<Logs>> LoadAllAsync(
            Func<SqliteConnection> openAppConnection,
            CancellationToken ct = default)
        {
            var items = new ObservableCollection<Logs>();
            using var cn = openAppConnection();

            long total = await CountAsync(cn, ct);
            Debug.WriteLine($"[LOGS] Total rows in Logs = {total}");

            using var cmd = cn.CreateCommand();
            cmd.CommandText = SqlCagegory.GetSql("Logs_Select_All.sql");

            using var rdr = await cmd.ExecuteReaderAsync(ct);
            while (await rdr.ReadAsync(ct))
                items.Add(Map(rdr));

            Debug.WriteLine($"[LOGS] LoadAllAsync loaded {items.Count} rows.");
            return items;
        }

        /// <summary>
        /// Load a page of logs (newest first).
        /// </summary>
        public static async Task<ObservableCollection<Logs>> LoadPageAsync(
            Func<SqliteConnection> openAppConnection,
            int skip, int take,
            CancellationToken ct = default)
        {
            var items = new ObservableCollection<Logs>();
            using var cn = openAppConnection();

            long total = await CountAsync(cn, ct);
            Debug.WriteLine($"[LOGS] Total rows in Logs = {total}");

            using var cmd = cn.CreateCommand();
            cmd.CommandText = SqlCagegory.GetSql("Logs_Select_Page.sql");
            cmd.Parameters.AddWithValue("$take", take);
            cmd.Parameters.AddWithValue("$skip", skip);

            using var rdr = await cmd.ExecuteReaderAsync(ct);
            while (await rdr.ReadAsync(ct))
                items.Add(Map(rdr));

            Debug.WriteLine($"[LOGS] LoadPageAsync loaded {items.Count} rows (take={take}, skip={skip}).");
            return items;
        }

        /// <summary>
        /// Clear and refill an existing bound collection in-place (newest first).
        /// </summary>
        public static async Task ReloadIntoAsync(
            ObservableCollection<Logs> target,
            Func<SqliteConnection> openAppConnection,
            CancellationToken ct = default)
        {
            target.Clear();
            using var cn = openAppConnection();

            long total = await CountAsync(cn, ct);
            Debug.WriteLine($"[LOGS] Total rows in Logs = {total}");

            using var cmd = cn.CreateCommand();
            cmd.CommandText = SqlCagegory.GetSql("Logs_Select_All.sql");

            int loaded = 0;
            using var rdr = await cmd.ExecuteReaderAsync(ct);
            while (await rdr.ReadAsync(ct))
            {
                target.Add(Map(rdr));
                loaded++;
            }

            Debug.WriteLine($"[LOGS] ReloadIntoAsync loaded {loaded} rows.");
        }

        // ---- helpers ----

        private static async Task<long> CountAsync(SqliteConnection cn, CancellationToken ct)
        {
            using var count = cn.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM Logs;";
            var obj = await count.ExecuteScalarAsync(ct);

            if (obj is long l) return l;
            if (obj is int i) return i;
            if (obj is string s && long.TryParse(s, out var p)) return p;
            return 0;
        }

        private static Logs Map(SqliteDataReader r)
        {
            return new Logs
            {
                Id = r.GetInt64(0),
                WhenUtc = GetString(r, 1),
                CreatedUtc = GetString(r, 2),
                Level = GetString(r, 3),
                Source = GetString(r, 4),
                EventCode = GetString(r, 5),
                SessionId = GetString(r, 6),
                MachineId = GetString(r, 7),
                AppVersion = GetString(r, 8),
                IsCrash = !r.IsDBNull(9) && (r.GetInt64(9) != 0),
                PayloadFmt = GetString(r, 10),
                PayloadVer = (int)(r.IsDBNull(11) ? 0 : r.GetInt64(11)),
                KeySetVersion = (int)(r.IsDBNull(12) ? 0 : r.GetInt64(12)),
                StackHash = GetString(r, 13),
                PayloadSize = r.IsDBNull(14) ? (int?)null : checked((int)r.GetInt64(14)),
            };
        }

        private static string GetString(SqliteDataReader r, int i)
            => r.IsDBNull(i) ? string.Empty : r.GetString(i);

        private static string AppVersion()
        {
            var v = System.Reflection.Assembly.GetEntryAssembly()?.GetName()?.Version;
            return v?.ToString() ?? "dev";
        }
    }
}
