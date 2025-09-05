using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using MWPV.Models;

namespace MWPV.Services
{
    /// <summary>
    /// Minimal loader for the log catalog (for binding in ViewLogs.xaml).
    /// Pass a factory that returns an OPEN SqliteConnection to the app DB.
    /// </summary>
    public static class LogCatalogService
    {
        /// <summary>
        /// Load ALL logs (ordered newest first) into a fresh collection.
        /// </summary>
        public static async Task<ObservableCollection<Logs>> LoadAllAsync(
            Func<SqliteConnection> openAppConnection,
            CancellationToken ct = default)
        {
            var items = new ObservableCollection<Logs>();

            using var cn = openAppConnection();

            // --- DEBUG: total row count ---------------------------------------
            long total = await CountAsync(cn, ct);
            Debug.WriteLine($"[LOGS] Total rows in Logs = {total}");
            // -------------------------------------------------------------------

            using var cmd = cn.CreateCommand();
            cmd.CommandText = @"
SELECT
  Id,
  WhenUtc,
  CreatedUtc,
  Level,
  Source,
  EventCode,
  SessionId,
  MachineId,
  AppVersion,
  IsCrash,
  PayloadFmt,
  PayloadVer,
  KeySetVersion,
  StackHash,
  length(Payload) as PayloadSize
FROM Logs
ORDER BY Id DESC;";

            using var rdr = await cmd.ExecuteReaderAsync(ct);
            while (await rdr.ReadAsync(ct))
                items.Add(Map(rdr));

            Debug.WriteLine($"[LOGS] LoadAllAsync loaded {items.Count} rows.");

            return items;
        }

        /// <summary>
        /// Load a page of logs (newest first). Useful if the table gets big.
        /// </summary>
        public static async Task<ObservableCollection<Logs>> LoadPageAsync(
            Func<SqliteConnection> openAppConnection,
            int skip, int take,
            CancellationToken ct = default)
        {
            var items = new ObservableCollection<Logs>();

            using var cn = openAppConnection();

            // --- DEBUG: total row count ---------------------------------------
            long total = await CountAsync(cn, ct);
            Debug.WriteLine($"[LOGS] Total rows in Logs = {total}");
            // -------------------------------------------------------------------

            using var cmd = cn.CreateCommand();
            cmd.CommandText = @"
SELECT
  Id,
  WhenUtc,
  CreatedUtc,
  Level,
  Source,
  EventCode,
  SessionId,
  MachineId,
  AppVersion,
  IsCrash,
  PayloadFmt,
  PayloadVer,
  KeySetVersion,
  StackHash,
  length(Payload) as PayloadSize
FROM Logs
ORDER BY Id DESC
LIMIT $take OFFSET $skip;";
            cmd.Parameters.AddWithValue("$take", take);
            cmd.Parameters.AddWithValue("$skip", skip);

            using var rdr = await cmd.ExecuteReaderAsync(ct);
            while (await rdr.ReadAsync(ct))
                items.Add(Map(rdr));

            Debug.WriteLine($"[LOGS] LoadPageAsync loaded {items.Count} rows (take={take}, skip={skip}).");

            return items;
        }

        /// <summary>
        /// Clear and refill an existing bound collection in-place.
        /// </summary>
        public static async Task ReloadIntoAsync(
            ObservableCollection<Logs> target,
            Func<SqliteConnection> openAppConnection,
            CancellationToken ct = default)
        {
            target.Clear();

            using var cn = openAppConnection();

            // --- DEBUG: total row count ---------------------------------------
            long total = await CountAsync(cn, ct);
            Debug.WriteLine($"[LOGS] Total rows in Logs = {total}");
            // -------------------------------------------------------------------

            using var cmd = cn.CreateCommand();
            cmd.CommandText = @"
SELECT
  Id,
  WhenUtc,
  CreatedUtc,
  Level,
  Source,
  EventCode,
  SessionId,
  MachineId,
  AppVersion,
  IsCrash,
  PayloadFmt,
  PayloadVer,
  KeySetVersion,
  StackHash,
  length(Payload) as PayloadSize
FROM Logs
ORDER BY Id DESC;";

            int loaded = 0;
            using var rdr = await cmd.ExecuteReaderAsync(ct);
            while (await rdr.ReadAsync(ct))
            {
                target.Add(Map(rdr));
                loaded++;
            }

            Debug.WriteLine($"[LOGS] ReloadIntoAsync loaded {loaded} rows into bound collection.");
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
            // SQLite returns integers as long; cast carefully.
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
    }
}
