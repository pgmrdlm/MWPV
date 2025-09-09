// MWPV/Services/LogCatalogService.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Utilities.Sql;       // SqlCagegory.GetSql(...)
using Utilities.Helpers;   // DatabaseHelper

namespace MWPV.Services
{
    /// <summary>
    /// Centralized reader/writer for Logs.
    /// - Write: Insert(RequestV3) matches Logs_Insert_V3.sql (loaded via SqlCagegory)
    /// - Read : SelectRecent(...) uses Logs_Select_Recent.sql (loaded via SqlCagegory)
    /// </summary>
    public static class LogCatalogService
    {
        // ---------------- WRITE ----------------

        public sealed class RequestV3
        {
            public string Level { get; set; } = "";
            public string Source { get; set; } = "";
            public string EventCode { get; set; } = "";
            public byte[]? Payload { get; set; } = null;
            public string PayloadFmt { get; set; } = "none";
            public string CreatedUtc { get; set; } = "";    // ISO-8601; if empty we fill
            public string AppVersion { get; set; } = "";

            // Optional extras
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
            public int? IsCrash { get; set; } = null; // 0/1
        }

        /// <summary>
        /// Single writer for logs. SQL text comes from SqlCagegory.GetSql("Logs_Insert_V3.sql").
        /// Returns inserted Id or -1 on failure.
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

                var createdIso = string.IsNullOrWhiteSpace(req.CreatedUtc) ? DateTime.UtcNow.ToString("o") : req.CreatedUtc!;
                var whenIso = string.IsNullOrWhiteSpace(req.WhenUtc) ? createdIso : req.WhenUtc!;

                cmd.Parameters.AddWithValue("@WhenUtc", whenIso);
                cmd.Parameters.AddWithValue("@CreatedUtc", createdIso);

                cmd.Parameters.AddWithValue("@Level", req.Level ?? "");
                cmd.Parameters.AddWithValue("@Source", req.Source ?? "");
                cmd.Parameters.AddWithValue("@EventCode", req.EventCode ?? "");

                cmd.Parameters.AddWithValue("@SessionId", req.SessionId ?? "");
                cmd.Parameters.AddWithValue("@LoginId", (object?)req.LoginId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ItemId", (object?)req.ItemId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@MachineId", Environment.MachineName ?? "");
                cmd.Parameters.AddWithValue("@DeviceMake", (object?)req.DeviceMake ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@DeviceModel", (object?)req.DeviceModel ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@OSVersion", (object?)req.OSVersion ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@DeviceIdHash", (object?)req.DeviceIdHash ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@InstallType", (object?)req.InstallType ?? DBNull.Value);

                cmd.Parameters.AddWithValue("@AppVersion",
                    string.IsNullOrWhiteSpace(req.AppVersion) ? AppVersion() : req.AppVersion);
                cmd.Parameters.AddWithValue("@IsCrash", req.IsCrash ?? 0);

                var pPayload = cmd.CreateParameter();
                pPayload.ParameterName = "@Payload";
                pPayload.Value = (object?)req.Payload ?? DBNull.Value;
                cmd.Parameters.Add(pPayload);

                cmd.Parameters.AddWithValue("@PayloadFmt",
                    string.IsNullOrWhiteSpace(req.PayloadFmt) ? "none" : req.PayloadFmt);
                cmd.Parameters.AddWithValue("@PayloadVer", (object?)req.PayloadVer ?? 0);
                cmd.Parameters.AddWithValue("@KeySetVersion", (object?)req.KeySetVersion ?? 0);
                cmd.Parameters.AddWithValue("@StackHash", req.StackHash ?? "");

                var affected = cmd.ExecuteNonQuery();
                if (affected != 1) return -1;

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

        // ---------------- READ ----------------

        /// <summary>
        /// Returns recent logs for the viewer. SQL loaded via SqlCagegory from Logs_Select_Recent.sql.
        /// We set both @Limit and @limit to be safe. Only populate properties guaranteed on your model.
        /// </summary>
        public static IReadOnlyList<MWPV.Models.Logs> SelectRecent(int limit = 200, Func<SqliteConnection>? openAppConnection = null)
        {
            openAppConnection ??= DatabaseHelper.GetAppOpenConnection;
            var result = new List<MWPV.Models.Logs>();

            try
            {
                using var cn = openAppConnection();
                using var cmd = cn.CreateCommand();

                var sql = SqlCagegory.GetSql("Logs_Select_Recent.sql");
                if (string.IsNullOrWhiteSpace(sql))
                    throw new InvalidOperationException("SQL not loaded: Logs_Select_Recent.sql");
                cmd.CommandText = sql;

                var pLimit = cmd.CreateParameter(); pLimit.ParameterName = "@Limit"; pLimit.Value = limit; cmd.Parameters.Add(pLimit);
                var pLower = cmd.CreateParameter(); pLower.ParameterName = "@limit"; pLower.Value = limit; cmd.Parameters.Add(pLower);

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var m = new MWPV.Models.Logs
                    {
                        Id = Convert.ToInt64(r["Id"]),
                        CreatedUtc = r["CreatedUtc"] as string ?? "",
                        Level = r["Level"] as string ?? "",
                        Source = r["Source"] as string ?? "",
                        EventCode = r["EventCode"] as string ?? ""   // NOTE: your model exposes EventCode (not Code)
                    };
                    result.Add(m);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LOGS][SelectRecent][FAIL] {ex.GetType().Name}: {ex.Message}");
            }

            return result;
        }
    }
}
