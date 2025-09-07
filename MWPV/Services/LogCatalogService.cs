// MWPV/Services/LogCatalogService.cs
using System;
using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Utilities.Sql;       // SqlCagegory.GetSql(...)
using Utilities.Helpers;   // DatabaseHelper (for default connection factory)

namespace MWPV.Services
{
    /// <summary>
    /// Single, centralized writer for Logs (matches Logs_Insert_V3.sql).
    /// Keep ALL param names here — nowhere else.
    /// </summary>
    public static class LogCatalogService
    {
        // Simple request bag so callers never touch SQL params.
        public sealed class RequestV3
        {
            public string Level { get; set; } = "";
            public string Source { get; set; } = "";
            public string EventCode { get; set; } = "";
            public byte[]? Payload { get; set; } = null;
            public string PayloadFmt { get; set; } = "none";
            public string CreatedUtc { get; set; } = "";    // ISO-8601; if empty we fill
            public string AppVersion { get; set; } = "";

            // Optional extras (safe defaults applied in Insert)
            public string? WhenUtc { get; set; } = null;     // if null -> mirror CreatedUtc
            public string? SessionId { get; set; } = null;
            public long? LoginId { get; set; } = null;
            public long? ItemId { get; set; } = null;
            public string? DeviceMake { get; set; } = null;
            public string? DeviceModel { get; set; } = null;
            public string? OSVersion { get; set; } = null;
            public string? DeviceIdHash { get; set; } = null;
            public string? InstallType { get; set; } = null;
            public int? PayloadVer { get; set; } = null;
            public int? KeySetVersion { get; set; } = null;
            public string? StackHash { get; set; } = null;
            public int? IsCrash { get; set; } = null;     // 0/1; default 0
        }

        /// <summary>
        /// The ONE writer. Returns inserted Id or -1.
        /// Matches exactly the columns/params in Logs_Insert_V3.sql.
        /// </summary>
        public static long Insert(RequestV3 req, Func<SqliteConnection>? openAppConnection = null)
        {
            openAppConnection ??= DatabaseHelper.GetAppOpenConnection;

            try
            {
                using var cn = openAppConnection();
                using var cmd = cn.CreateCommand();

                var sql = SqlCagegory.GetSql("Logs_Insert_V3.sql");
                if (string.IsNullOrWhiteSpace(sql))
                    throw new InvalidOperationException("SQL not loaded: Logs_Insert_V3.sql");
                cmd.CommandText = sql;

                // --- Times ---
                var createdIso = string.IsNullOrWhiteSpace(req.CreatedUtc) ? DateTime.UtcNow.ToString("o") : req.CreatedUtc!;
                var whenIso = string.IsNullOrWhiteSpace(req.WhenUtc) ? createdIso : req.WhenUtc!;

                cmd.Parameters.AddWithValue("@WhenUtc", whenIso);
                cmd.Parameters.AddWithValue("@CreatedUtc", createdIso);

                // --- Core ---
                cmd.Parameters.AddWithValue("@Level", req.Level ?? "");
                cmd.Parameters.AddWithValue("@Source", req.Source ?? "");
                cmd.Parameters.AddWithValue("@EventCode", req.EventCode ?? "");

                // --- Session / User / Device (safe defaults) ---
                cmd.Parameters.AddWithValue("@SessionId", req.SessionId ?? "");
                cmd.Parameters.AddWithValue("@LoginId", (object?)req.LoginId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ItemId", (object?)req.ItemId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@MachineId", Environment.MachineName ?? "");
                cmd.Parameters.AddWithValue("@DeviceMake", (object?)req.DeviceMake ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@DeviceModel", (object?)req.DeviceModel ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@OSVersion", (object?)req.OSVersion ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@DeviceIdHash", (object?)req.DeviceIdHash ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@InstallType", (object?)req.InstallType ?? DBNull.Value);

                // --- App / Crash flag ---
                cmd.Parameters.AddWithValue("@AppVersion",
                    string.IsNullOrWhiteSpace(req.AppVersion) ? AppVersion() : req.AppVersion);
                cmd.Parameters.AddWithValue("@IsCrash", req.IsCrash ?? 0);

                // --- Payload ---
                var pPayload = cmd.CreateParameter();
                pPayload.ParameterName = "@Payload";
                pPayload.Value = (object?)req.Payload ?? DBNull.Value;
                cmd.Parameters.Add(pPayload);

                cmd.Parameters.AddWithValue("@PayloadFmt",
                    string.IsNullOrWhiteSpace(req.PayloadFmt) ? "none" : req.PayloadFmt);
                cmd.Parameters.AddWithValue("@PayloadVer", (object?)req.PayloadVer ?? 0);
                cmd.Parameters.AddWithValue("@KeySetVersion", (object?)req.KeySetVersion ?? 0);
                cmd.Parameters.AddWithValue("@StackHash", req.StackHash ?? "");

                // --- INSERT ---
                var affected = cmd.ExecuteNonQuery();
                if (affected != 1)
                {
                    Debug.WriteLine("[LOGS][Insert] affected != 1");
                    return -1;
                }

#if DEBUG
                // --- DEBUG: print total count after insert ---
                try
                {
                    using var countCmd = cn.CreateCommand();
                    countCmd.CommandText = "SELECT COUNT(*) FROM Logs;";
                    var total = Convert.ToInt64(countCmd.ExecuteScalar());
                    Debug.WriteLine($"[LOGS][DEBUG] total rows now = {total}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LOGS][DEBUG][COUNT][FAIL] {ex.GetType().Name}: {ex.Message}");
                }
#endif

                // --- last insert id ---
                using var last = cn.CreateCommand();
                last.CommandText = SqlCagegory.GetSql("Logs_LastInsertId.sql");
                return Convert.ToInt64(last.ExecuteScalar());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LOGS][Insert][FAIL] {ex.GetType().Name}: {ex.Message}");
                return -1;
            }
        }

        /// <summary>
        /// Convenience: login heartbeat using this same writer (no payload).
        /// </summary>
        public static long InsertLoginEvent(Func<SqliteConnection>? openAppConnection = null)
        {
            var req = new RequestV3
            {
                Level = "INFO",
                Source = "Login",
                EventCode = "LOGIN",
                Payload = null,
                PayloadFmt = "none",
                AppVersion = AppVersion()
            };
            return Insert(req, openAppConnection ?? DatabaseHelper.GetAppOpenConnection);
        }

        private static string AppVersion()
        {
            var v = System.Reflection.Assembly.GetEntryAssembly()?.GetName()?.Version;
            return v?.ToString() ?? "dev";
        }
    }
}
