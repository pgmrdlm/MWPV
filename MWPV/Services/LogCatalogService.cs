using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Utilities.Sql;       // SqlCagegory.GetSql(...)
using Utilities.Helpers;   // DatabaseHelper
using MWPV.Utilities.Json; // AppJson

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
            public byte[]? Payload { get; set; } = null;
            public string PayloadFmt { get; set; } = "none";
            public string CreatedUtc { get; set; } = "";
            public string AppVersion { get; set; } = "";

            // Optional extras
            public string? WhenUtc { get; set; } = null;
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
                cmd.Parameters.AddWithValue("@PayloadVer", (object?)req.PayloadVer ?? 1);
                cmd.Parameters.AddWithValue("@KeySetVersion", (object?)req.KeySetVersion ?? 1);
                cmd.Parameters.AddWithValue("@StackHash", req.StackHash ?? "");

                var affected = cmd.ExecuteNonQuery();
                if (affected != 1) return -1;

                using var last = cn.CreateCommand();
                last.CommandText = SqlCagegory.GetSql("Logs_LastInsertId.sql");
                return Convert.ToInt64(last.ExecuteScalar(), CultureInfo.InvariantCulture);
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

        public static long AppendJson(
            string level,
            string source,
            string eventCode,
            object dto,
            DateTime? whenUtc = null,
            string? sessionId = null,
            long? loginId = null,
            long? itemId = null,
            int payloadVer = 1,
            int? keySetVersion = null,
            bool isCrash = false,
            Func<SqliteConnection>? openAppConnection = null)
        {
            var json = Encoding.UTF8.GetBytes(AppJson.Serialize(dto, pretty: false));

            var req = new RequestV3
            {
                Level = level ?? "INFO",
                Source = source ?? "",
                EventCode = eventCode ?? "",
                Payload = json,
                PayloadFmt = "json",
                PayloadVer = payloadVer,
                KeySetVersion = keySetVersion,
                AppVersion = AppVersion(),
                CreatedUtc = DateTime.UtcNow.ToString("o"),
                WhenUtc = (whenUtc ?? DateTime.UtcNow).ToUniversalTime().ToString("o"),
                SessionId = sessionId,
                LoginId = loginId,
                ItemId = itemId,
                IsCrash = isCrash ? 1 : 0
            };

            return Insert(req, openAppConnection);
        }

        public static long AppendJson(
            string level,
            string source,
            string eventCode,
            AppJson.LogPayloadDto dto,
            DateTime? whenUtc = null,
            string? sessionId = null,
            long? loginId = null,
            long? itemId = null,
            int payloadVer = 1,
            int? keySetVersion = null,
            bool isCrash = false,
            Func<SqliteConnection>? openAppConnection = null)
            => AppendJson(level, source, eventCode, (object)dto, whenUtc, sessionId, loginId, itemId, payloadVer, keySetVersion, isCrash, openAppConnection);

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

                var sql = SqlCagegory.GetSql("Logs_Select_Page.sql");
                if (string.IsNullOrWhiteSpace(sql))
                    throw new InvalidOperationException("SQL not loaded: Logs_Select_Page.sql");
                cmd.CommandText = sql;

                var pLimit = cmd.CreateParameter(); pLimit.ParameterName = "@limit"; pLimit.Value = limit; cmd.Parameters.Add(pLimit);
                var pOffset = cmd.CreateParameter(); pOffset.ParameterName = "@offset"; pOffset.Value = offset; cmd.Parameters.Add(pOffset);

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    result.Add(new global::MWPV.Models.Logs
                    {
                        Id = Convert.ToInt64(r["Id"], CultureInfo.InvariantCulture),
                        CreatedUtc = r["CreatedUtc"] as string ?? "",
                        Level = r["Level"] as string ?? "",
                        Source = r["Source"] as string ?? "",
                        EventCode = r["EventCode"] as string ?? ""
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LOGS][SelectPage][FAIL] {ex.GetType().Name}: {ex.Message}");
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

                var sql = SqlCagegory.GetSql("Logs_Select_Page_Filter.sql");
                if (string.IsNullOrWhiteSpace(sql))
                    throw new InvalidOperationException("SQL not loaded: Logs_Select_Page_Filter.sql");
                cmd.CommandText = sql;

                var pLimit = cmd.CreateParameter(); pLimit.ParameterName = "@limit"; pLimit.Value = limit; cmd.Parameters.Add(pLimit);
                var pOffset = cmd.CreateParameter(); pOffset.ParameterName = "@offset"; pOffset.Value = offset; cmd.Parameters.Add(pOffset);

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
                        EventCode = r["EventCode"] as string ?? ""
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LOGS][SelectPageFiltered][FAIL] {ex.GetType().Name}: {ex.Message}");
            }

            return result;
        }

        public static IReadOnlyList<global::MWPV.Models.Logs> SelectRecent(
            int limit = 200,
            Func<SqliteConnection>? openAppConnection = null)
            => SelectPage(offset: 0, limit: limit, openAppConnection);

        public static IReadOnlyList<global::MWPV.Models.Logs> SelectRecent(string? filterCode, int limit = 200,
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
            public string? PayloadFmt { get; init; }
            public int PayloadSize { get; init; }
            public string? Payload { get; init; }
        }

        public static LogDetailsRecord? SelectById(long id, Func<SqliteConnection>? openAppConnection = null)
        {
            openAppConnection ??= DatabaseHelper.GetAppOpenConnection;

            try
            {
                using var cn = openAppConnection();
                using var cmd = cn.CreateCommand();

                var sql = SqlCagegory.GetSql("Logs_Select_ById.sql");
                if (string.IsNullOrWhiteSpace(sql))
                    throw new InvalidOperationException("SQL not loaded: Logs_Select_ById.sql");
                cmd.CommandText = sql;

                cmd.Parameters.AddWithValue("@id", id);

                using var r = cmd.ExecuteReader();
                if (!r.Read()) return null;

                var createdUtc = ReadString(r, "CreatedUtc") ?? "";
                var level = ReadString(r, "Level") ?? "";
                var source = ReadString(r, "Source") ?? "";
                var eventCode = ReadString(r, "EventCode") ?? "";
                var payloadFmt = ReadString(r, "PayloadFmt") ?? "none";
                var payloadSizeC = ReadInt32Nullable(r, "PayloadSize") ?? 0;
                var blob = ReadBytesNullable(r, "Payload");

                var payloadSize = blob?.Length ?? payloadSizeC;
                string? payloadText = DecodePayloadText(payloadFmt, blob);

                return new LogDetailsRecord
                {
                    Id = Convert.ToInt64(r["Id"], CultureInfo.InvariantCulture),
                    CreatedUtc = createdUtc,
                    Level = level,
                    Source = source,
                    EventCode = eventCode,
                    PayloadFmt = payloadFmt,
                    PayloadSize = payloadSize,
                    Payload = payloadText
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LOGS][SelectById][FAIL] {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        // =====================================================================
        // Filter options (ComboType/ComboDetail) via packed SQL
        // =====================================================================

        // Legacy lightweight DTO (kept for backward-compat in existing callers)
        public sealed class ComboItem
        {
            public int Id { get; init; }              // ComboDet
            public string Code { get; init; } = "";
            public string Description { get; init; } = "";
            public int Seq { get; init; }
        }

        /// <summary>
        /// New: return full model rows for a given ComboType.Code (e.g., "log_filters").
        /// SQL: ComboDetail_SelectByType.sql
        /// </summary>
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

                var sql = SqlCagegory.GetSql("ComboDetail_SelectByType.sql");
                if (string.IsNullOrWhiteSpace(sql))
                    throw new InvalidOperationException("SQL not loaded: ComboDetail_SelectByType.sql");
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
                Debug.WriteLine($"[LOGS][GetComboDetailsByType:{comboTypeCode}][FAIL] {ex.GetType().Name}: {ex.Message}");
            }

            return list;
        }

        /// <summary>
        /// Back-compat wrapper: map model rows to the legacy lightweight DTO.
        /// </summary>
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

        private static string? DecodePayloadText(string? fmt, byte[]? bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;

            switch ((fmt ?? "none").ToLowerInvariant())
            {
                case "json":
                case "text":
                    return Encoding.UTF8.GetString(bytes);
                default:
                    return null;
            }
        }

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

        private static int? ReadInt32Nullable(SqliteDataReader r, string name)
        {
            var ord = SafeOrdinal(r, name);
            if (ord < 0 || r.IsDBNull(ord)) return null;
            return r.GetInt32(ord);
        }

        private static byte[]? ReadBytesNullable(SqliteDataReader r, string name)
        {
            var ord = SafeOrdinal(r, name);
            if (ord < 0 || r.IsDBNull(ord)) return null;

            var len = (int)r.GetBytes(ord, 0, null, 0, 0);
            var buf = new byte[len];
            r.GetBytes(ord, 0, buf, 0, len);
            return buf;
        }

        private static int SafeOrdinal(SqliteDataReader r, string name)
        {
            try { return r.GetOrdinal(name); }
            catch { return -1; }
        }
    }
}
