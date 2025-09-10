using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Utilities.Sql;       // SqlCagegory.GetSql(...)
using Utilities.Helpers;   // DatabaseHelper
using MWPV.Utilities.Json; // AppJson.LogPayloadDto (back-compat)

namespace MWPV.Services
{
    public static class LogCatalogService
    {
        // ---------------- WRITE (core) ----------------

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
                cmd.Parameters.AddWithValue("@PayloadVer", (object?)req.PayloadVer ?? 0);
                cmd.Parameters.AddWithValue("@KeySetVersion", (object?)req.KeySetVersion ?? 0);
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

        // ---------------- WRITE (helpers) ----------------

        public static long AppendJson(
            string level,
            string source,
            string eventCode,
            object dto,
            DateTime? whenUtc = null,
            string? sessionId = null,
            long? loginId = null,
            long? itemId = null,
            int? payloadVer = 1,
            int? keySetVersion = null,
            bool isCrash = false,
            Func<SqliteConnection>? openAppConnection = null)
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(dto, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

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
            int? payloadVer = 1,
            int? keySetVersion = null,
            bool isCrash = false,
            Func<SqliteConnection>? openAppConnection = null)
            => AppendJson(level, source, eventCode, (object)dto, whenUtc, sessionId, loginId, itemId, payloadVer, keySetVersion, isCrash, openAppConnection);

        // ---------------- READ ----------------

        // lightweight list for the grid (no payload fields)
        public static IReadOnlyList<global::MWPV.Models.Logs> SelectRecent(int limit = 200, Func<SqliteConnection>? openAppConnection = null)
        {
            openAppConnection ??= DatabaseHelper.GetAppOpenConnection;
            var result = new List<global::MWPV.Models.Logs>();

            try
            {
                using var cn = openAppConnection();
                using var cmd = cn.CreateCommand();

                var sql = SqlCagegory.GetSql("Logs_Select_Recent.sql");
                if (string.IsNullOrWhiteSpace(sql))
                    throw new InvalidOperationException("SQL not loaded: Logs_Select_Recent.sql");
                cmd.CommandText = sql;

                // support @Limit or @limit, depending on the SQL text
                var pLimit = cmd.CreateParameter(); pLimit.ParameterName = "@Limit"; pLimit.Value = limit; cmd.Parameters.Add(pLimit);
                var pLower = cmd.CreateParameter(); pLower.ParameterName = "@limit"; pLower.Value = limit; cmd.Parameters.Add(pLower);

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var m = new global::MWPV.Models.Logs
                    {
                        Id = Convert.ToInt64(r["Id"], CultureInfo.InvariantCulture),
                        CreatedUtc = r["CreatedUtc"] as string ?? "",
                        Level = r["Level"] as string ?? "",
                        Source = r["Source"] as string ?? "",
                        EventCode = r["EventCode"] as string ?? ""
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

        // ==== details DTO (schema has no Message column) ====
        public sealed class LogDetailsRecord
        {
            public long Id { get; init; }
            public string CreatedUtc { get; init; } = "";
            public string Level { get; init; } = "";
            public string Source { get; init; } = "";
            public string EventCode { get; init; } = "";
            public string? PayloadFmt { get; init; }
            public int PayloadSize { get; init; }
            public string? Payload { get; init; }  // decoded text for json/text
        }

        // full record for details (includes payload)
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

                string createdUtc = ReadString(r, "CreatedUtc") ?? "";
                string level = ReadString(r, "Level") ?? "";
                string source = ReadString(r, "Source") ?? "";
                string eventCode = ReadString(r, "EventCode") ?? "";
                string payloadFmt = ReadString(r, "PayloadFmt") ?? "none";
                int payloadSizeCol = ReadInt32Nullable(r, "PayloadSize") ?? 0;
                byte[]? blob = ReadBytesNullable(r, "Payload");
                string? payloadTxt = DecodePayloadText(payloadFmt, blob);

                int payloadSize = blob?.Length ?? payloadSizeCol;

                return new LogDetailsRecord
                {
                    Id = Convert.ToInt64(r["Id"], CultureInfo.InvariantCulture),
                    CreatedUtc = createdUtc,
                    Level = level,
                    Source = source,
                    EventCode = eventCode,
                    PayloadFmt = payloadFmt,
                    PayloadSize = payloadSize,
                    Payload = payloadTxt
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LOGS][SelectById][FAIL] {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        // ---------------- helpers ----------------

        private static string? DecodePayloadText(string? fmt, byte[]? bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;

            switch ((fmt ?? "none").ToLowerInvariant())
            {
                case "json":
                case "text":
                    return Encoding.UTF8.GetString(bytes);
                case "none":
                default:
                    try { return Encoding.UTF8.GetString(bytes); }
                    catch { return null; }
            }
        }

        private static string? ReadString(SqliteDataReader r, string name)
        {
            var ord = SafeOrdinal(r, name);
            if (ord < 0 || r.IsDBNull(ord)) return null;
            return r.GetString(ord);
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
