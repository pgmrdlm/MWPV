//
// File: Services/LogCatalogService.cs
//
// FULL REWRITE
//
// Adds READ support for LogMessageTemplate via:
// - s_LogMessageTemplate_SelectAll.sql
// - Models.LogMessageTemplate
//
// Adds WRITE support for:
// - Logs.SubjectText
// - Logs.MessageText
//
// Notes:
// - Templates are non-sensitive and safe to hold in memory.
// - This service returns the rows; caching strategy is a separate step.
//

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Utilities.Sql;       // SqlCagegory.GetSql(...)
using Utilities.Helpers;   // DatabaseHelper

namespace MWPV.Services
{
    public static class LogCatalogService
    {
        // =====================================================================
        // WRITE
        // =====================================================================

        public sealed class RequestV3
        {
            public string Level { get; set; } = "";
            public string Source { get; set; } = "";
            public string EventCode { get; set; } = "";
            public string CreatedUtc { get; set; } = "";
            public string AppVersion { get; set; } = "";

            // Optional extras (kept because they were already part of your insert wiring)
            public string? WhenUtc { get; set; } = null;
            public string? SessionId { get; set; } = null;
            public long? LoginId { get; set; } = null;
            public long? ItemId { get; set; } = null;

            // NEW: non-sensitive log rendering fields
            public string? SubjectText { get; set; } = null;
            public string? MessageText { get; set; } = null;

            public string? DeviceMake { get; set; } = null;
            public string? DeviceModel { get; set; } = null;
            public string? OSVersion { get; set; } = null;
            public string? DeviceIdHash { get; set; } = null;
            public string? InstallType { get; set; } = null;
            public string? StackHash { get; set; } = null;
            public int? IsCrash { get; set; } = null; // 0/1

            // -----------------------------------------------------------------
            // Payload STUBS (NO-OP for now)
            // -----------------------------------------------------------------
            // These exist ONLY so older callers (EarlyLogIngester, old code paths)
            // still compile. They are not written to the DB because the DDL/SQL
            // no longer contains payload columns/params.
            public byte[]? Payload { get; set; } = null;
            public string PayloadFmt { get; set; } = "none";
            public int? PayloadVer { get; set; } = null;
            public int? KeySetVersion { get; set; } = null;
            // -----------------------------------------------------------------
        }

        public static long Insert(RequestV3 req, Func<SqliteConnection>? openAppConnection = null)
        {
            openAppConnection ??= DatabaseHelper.GetAppOpenConnection;

            try
            {
                using var cn = openAppConnection();
                using var cmd = cn.CreateCommand();

                var sql = SqlCagegory.GetSql("s_Logs_Insert.sql");
                if (string.IsNullOrWhiteSpace(sql))
                    throw new InvalidOperationException("SQL not loaded: s_Logs_Insert.sql");

                cmd.CommandText = sql;

                var createdIso = string.IsNullOrWhiteSpace(req.CreatedUtc)
                    ? DateTime.UtcNow.ToString("o")
                    : req.CreatedUtc!;

                var whenIso = string.IsNullOrWhiteSpace(req.WhenUtc)
                    ? createdIso
                    : req.WhenUtc!;

                // NOTE: Parameter list MUST match s_Logs_Insert.sql.
                // We intentionally do NOT bind any payload-related params.

                cmd.Parameters.AddWithValue("@WhenUtc", whenIso);
                cmd.Parameters.AddWithValue("@CreatedUtc", createdIso);

                cmd.Parameters.AddWithValue("@Level", req.Level ?? "");
                cmd.Parameters.AddWithValue("@Source", req.Source ?? "");
                cmd.Parameters.AddWithValue("@EventCode", req.EventCode ?? "");

                cmd.Parameters.AddWithValue("@SessionId", req.SessionId ?? "");
                cmd.Parameters.AddWithValue("@LoginId", (object?)req.LoginId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ItemId", (object?)req.ItemId ?? DBNull.Value);

                // NEW: non-sensitive rendered fields
                cmd.Parameters.AddWithValue("@SubjectText", (object?)req.SubjectText ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@MessageText", (object?)req.MessageText ?? DBNull.Value);

                cmd.Parameters.AddWithValue("@MachineId", Environment.MachineName ?? "");
                cmd.Parameters.AddWithValue("@DeviceMake", (object?)req.DeviceMake ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@DeviceModel", (object?)req.DeviceModel ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@OSVersion", (object?)req.OSVersion ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@DeviceIdHash", (object?)req.DeviceIdHash ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@InstallType", (object?)req.InstallType ?? DBNull.Value);

                cmd.Parameters.AddWithValue("@AppVersion",
                    string.IsNullOrWhiteSpace(req.AppVersion) ? AppVersion() : req.AppVersion);

                cmd.Parameters.AddWithValue("@IsCrash", req.IsCrash ?? 0);
                cmd.Parameters.AddWithValue("@StackHash", req.StackHash ?? "");
                // KeySetVersion is REQUIRED by s_Logs_Insert.sql
                cmd.Parameters.AddWithValue("@KeySetVersion", req.KeySetVersion ?? 1);

                var affected = cmd.ExecuteNonQuery();
                if (affected != 1) return -1;

                using var last = cn.CreateCommand();
                last.CommandText = SqlCagegory.GetSql("s_Logs_LastInsertId.sql");
                return Convert.ToInt64(last.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
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
                AppVersion = AppVersion()
            };
            return Insert(req, openAppConnection ?? DatabaseHelper.GetAppOpenConnection);
        }

        public static long InsertSessionStart(Func<SqliteConnection>? openAppConnection = null)
        {
            var req = new RequestV3
            {
                Level = "INFO",
                Source = "Session",
                EventCode = "SESSION_START",
                AppVersion = AppVersion()
            };
            return Insert(req, openAppConnection ?? DatabaseHelper.GetAppOpenConnection);
        }

        public static long InsertSessionEnd(
            string reason = "NormalExit",
            bool isError = false,
            int? exitCode = null,
            Func<SqliteConnection>? openAppConnection = null)
        {
            // Still no payload. Later, when we implement edit-change logging,
            // we can add dedicated non-sensitive columns or an encrypted blob approach.
            var req = new RequestV3
            {
                Level = isError ? "ERROR" : "INFO",
                Source = "Session",
                EventCode = "SESSION_END",
                AppVersion = AppVersion()
            };
            return Insert(req, openAppConnection ?? DatabaseHelper.GetAppOpenConnection);
        }

        private static string AppVersion()
        {
            var v = System.Reflection.Assembly.GetEntryAssembly()?.GetName()?.Version;
            return v?.ToString() ?? "dev";
        }

        // ---------------------------------------------------------------------
        // AppendJson STUB (NO PAYLOAD WRITES)
        // ---------------------------------------------------------------------
        // Kept ONLY to satisfy existing callers. We accept the dto but do not store it.
        // When we implement edit/change logs later, we’ll replace this with the new logic.

        // =====================================================================
        // READ
        // =====================================================================

        public static IReadOnlyList<global::MWPV.Models.Logs> SelectPage(
            int offset,
            int limit,
            Func<SqliteConnection>? openAppConnection = null)
        {
            openAppConnection ??= DatabaseHelper.GetAppOpenConnection;
            var result = new List<global::MWPV.Models.Logs>();

            try
            {
                using var cn = openAppConnection();
                using var cmd = cn.CreateCommand();

                var sql = SqlCagegory.GetSql("s_Logs_SelectPage.sql");
                if (string.IsNullOrWhiteSpace(sql))
                    throw new InvalidOperationException("SQL not loaded: s_Logs_SelectPage.sql");
                cmd.CommandText = sql;

                cmd.Parameters.AddWithValue("@limit", limit);
                cmd.Parameters.AddWithValue("@offset", offset);

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    result.Add(new global::MWPV.Models.Logs
                    {
                        Id = Convert.ToInt64(r["Id"], CultureInfo.InvariantCulture),
                        CreatedUtc = r["CreatedUtc"] as string ?? "",
                        Level = r["Level"] as string ?? "",
                        Source = r["Source"] as string ?? "",
                        EventCode = r["EventCode"] as string ?? "",

                        // NEW fields for UI details pane / grid binding
                        SubjectText = r["SubjectText"] as string,
                        MessageText = r["MessageText"] as string,

                        // Keep legacy binders alive
                        Message = r["MessageText"] as string
                    });
                }
            }
            catch (Exception ex)
            {
            }

            return result;
        }

        public static IReadOnlyList<global::MWPV.Models.Logs> SelectPageFiltered(
            int offset,
            int limit,
            string? filterCode,
            Func<SqliteConnection>? openAppConnection = null)
        {
            openAppConnection ??= DatabaseHelper.GetAppOpenConnection;
            var result = new List<global::MWPV.Models.Logs>();

            try
            {
                using var cn = openAppConnection();
                using var cmd = cn.CreateCommand();

                var sql = SqlCagegory.GetSql("s_Logs_SelectPageFilter.sql");
                if (string.IsNullOrWhiteSpace(sql))
                    throw new InvalidOperationException("SQL not loaded: s_Logs_SelectPageFilter.sql");
                cmd.CommandText = sql;

                cmd.Parameters.AddWithValue("@limit", limit);
                cmd.Parameters.AddWithValue("@offset", offset);

                var filterParam = cmd.CreateParameter();
                filterParam.ParameterName = "@filter_code";
                filterParam.Value = string.IsNullOrWhiteSpace(filterCode) ? DBNull.Value : filterCode!;
                cmd.Parameters.Add(filterParam);

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    result.Add(new global::MWPV.Models.Logs
                    {
                        Id = Convert.ToInt64(r["Id"], CultureInfo.InvariantCulture),
                        CreatedUtc = r["CreatedUtc"] as string ?? "",
                        Level = r["Level"] as string ?? "",
                        Source = r["Source"] as string ?? "",
                        EventCode = r["EventCode"] as string ?? "",

                        // NEW fields for UI details pane / grid binding
                        SubjectText = r["SubjectText"] as string,
                        MessageText = r["MessageText"] as string,

                        // Keep legacy binders alive
                        Message = r["MessageText"] as string
                    });
                }
            }
            catch (Exception ex)
            {
            }

            return result;
        }

        public static IReadOnlyList<global::MWPV.Models.Logs> SelectRecent(
            int limit = 200,
            Func<SqliteConnection>? openAppConnection = null)
            => SelectPage(offset: 0, limit: limit, openAppConnection);

        public static IReadOnlyList<global::MWPV.Models.Logs> SelectRecent(
            string? filterCode,
            int limit = 200,
            Func<SqliteConnection>? openAppConnection = null)
            => SelectPageFiltered(offset: 0, limit: limit, filterCode: filterCode, openAppConnection);

        // ---- Details DTO returned to the window ----
        public sealed class LogDetailsRecord
        {
            public long Id { get; init; }
            public string CreatedUtc { get; init; } = "";
            public string Level { get; init; } = "";
            public string Source { get; init; } = "";
            public string EventCode { get; init; } = "";

            public string? SubjectText { get; init; }
            public string? MessageText { get; init; }
        }

        public static LogDetailsRecord? SelectById(long id, Func<SqliteConnection>? openAppConnection = null)
        {
            openAppConnection ??= DatabaseHelper.GetAppOpenConnection;

            try
            {
                using var cn = openAppConnection();
                using var cmd = cn.CreateCommand();

                var sql = SqlCagegory.GetSql("s_Logs_SelectById.sql");
                if (string.IsNullOrWhiteSpace(sql))
                    throw new InvalidOperationException("SQL not loaded: s_Logs_SelectById.sql");
                cmd.CommandText = sql;

                cmd.Parameters.AddWithValue("@id", id);

                using var r = cmd.ExecuteReader();
                if (!r.Read()) return null;

                return new LogDetailsRecord
                {
                    Id = Convert.ToInt64(r["Id"], CultureInfo.InvariantCulture),
                    CreatedUtc = r["CreatedUtc"] as string ?? "",
                    Level = r["Level"] as string ?? "",
                    Source = r["Source"] as string ?? "",
                    EventCode = r["EventCode"] as string ?? "",

                    SubjectText = r["SubjectText"] as string,
                    MessageText = r["MessageText"] as string
                };
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        // =====================================================================
        // Log Message Templates (LogMessageTemplate)
        // =====================================================================

        public static IReadOnlyList<global::MWPV.Models.LogMessageTemplate> SelectActiveLogMessageTemplates(
            Func<SqliteConnection>? openAppConnection = null)
        {
            openAppConnection ??= DatabaseHelper.GetAppOpenConnection;
            var list = new List<global::MWPV.Models.LogMessageTemplate>();

            try
            {
                using var cn = openAppConnection();
                using var cmd = cn.CreateCommand();

                var sql = SqlCagegory.GetSql("s_LogMessageTemplate_SelectAll.sql");
                if (string.IsNullOrWhiteSpace(sql))
                    throw new InvalidOperationException("SQL not loaded: s_LogMessageTemplate_SelectAll.sql");

                cmd.CommandText = sql;

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var activeInt = SafeGetInt32(r, "Active");

                    list.Add(new global::MWPV.Models.LogMessageTemplate
                    {
                        UpdateForm = ReadString(r, "UpdateForm") ?? "",
                        Seq = SafeGetInt32(r, "Seq"),
                        LogMessage = ReadString(r, "LogMessage") ?? "",
                        Active = (activeInt == 1)
                    });
                }
            }
            catch (Exception ex)
            {
            }

            return list;
        }

        // =====================================================================
        // Filter options (ComboType/ComboDetail) via packed SQL
        // =====================================================================

        public sealed class ComboItem
        {
            public int Id { get; init; }              // ComboDet
            public string Code { get; init; } = "";
            public string Description { get; init; } = "";
            public int Seq { get; init; }
        }

        public static IReadOnlyList<global::MWPV.Models.ComboDetail> GetComboDetailsByType(
            string comboTypeCode,
            Func<SqliteConnection>? openAppConnection = null)
        {
            openAppConnection ??= DatabaseHelper.GetAppOpenConnection;
            var list = new List<global::MWPV.Models.ComboDetail>();

            try
            {
                using var cn = openAppConnection();
                using var cmd = cn.CreateCommand();

                var sql = SqlCagegory.GetSql("s_Combo_LogsDetailSelectByType.sql");
                if (string.IsNullOrWhiteSpace(sql))
                    throw new InvalidOperationException("SQL not loaded: s_Combo_LogsDetailSelectByType.sql");
                cmd.CommandText = sql;

                cmd.Parameters.AddWithValue("@type_code", comboTypeCode);

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    list.Add(new global::MWPV.Models.ComboDetail
                    {
                        ComboDet = SafeGetInt32(r, "ComboDet"),
                        ComboTyp = SafeGetInt32(r, "ComboTyp"),
                        Seq = SafeGetInt32(r, "Seq"),
                        Code = ReadString(r, "Code") ?? "",
                        Description = ReadString(r, "Description") ?? "",
                        Active = SafeGetInt32(r, "Active"),
                        CreatedUtc = ReadString(r, "CreatedUtc") ?? "",
                        UpdatedUtc = ReadString(r, "UpdatedUtc") ?? ""
                    });
                }
            }
            catch (Exception ex)
            {
            }

            return list;
        }

        public static IReadOnlyList<ComboItem> GetComboItems(
            string comboTypeCode,
            Func<SqliteConnection>? openAppConnection = null)
        {
            var rows = GetComboDetailsByType(comboTypeCode, openAppConnection);
            var list = new List<ComboItem>(rows.Count);
            foreach (var r in rows)
            {
                list.Add(new ComboItem
                {
                    Id = r.ComboDet,
                    Code = r.Code,
                    Description = r.Description,
                    Seq = r.Seq
                });
            }
            return list;
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        private static string? ReadString(SqliteDataReader r, string name)
        {
            var ord = SafeOrdinal(r, name);
            if (ord < 0 || r.IsDBNull(ord)) return null;
            return r.GetString(ord);
        }

        private static int SafeGetInt32(SqliteDataReader r, string name)
        {
            var ord = SafeOrdinal(r, name);
            if (ord < 0 || r.IsDBNull(ord)) return 0;
            return r.GetInt32(ord);
        }

        private static int SafeOrdinal(SqliteDataReader r, string name)
        {
            try { return r.GetOrdinal(name); }
            catch { return -1; }
        }
    }
}
