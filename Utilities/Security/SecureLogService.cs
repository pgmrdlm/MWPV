using System;
using System.Data;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Utilities.Logging;        // LogSeverity
using Utilities.Security;       // SecureEncryptedDataStore

namespace Utilities.Security
{
    /// <summary>
    /// Centralized encrypted app logging to SQLite (v2 schema).
    /// </summary>
    public static class SecureLogService
    {
        // ---- configuration ----
        private static Func<SqliteConnection>? _connectionFactory;
        private static string _appVersion = "unknown";
        private static string _machineId = Environment.MachineName;
        private static readonly JsonSerializerOptions _json = new() { WriteIndented = false };

        // Session id (fallback if the host doesn’t pass one in payload)
        private static readonly string _sessionId = Guid.NewGuid().ToString("N");

        /// <summary>Call once at startup.</summary>
        public static void Initialize(
            Func<SqliteConnection> connectionFactory,
            string appVersion,
            string machineId)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _appVersion = string.IsNullOrWhiteSpace(appVersion) ? "unknown" : appVersion;
            _machineId = string.IsNullOrWhiteSpace(machineId) ? Environment.MachineName : machineId;
        }

        /// <summary>
        /// Write a log row (v2).
        /// Named args supported: eventCode, source, whenUtc, createdUtc, isCrash, stackHash, payloadFmt.
        /// </summary>
        public static async Task WriteAsync(
            LogSeverity level,
            object? payload,
            string? eventCode = null,
            string? source = null,
            DateTime? whenUtc = null,
            DateTime? createdUtc = null,
            bool isCrash = false,
            string? stackHash = null,
            string? payloadFmt = "json")
        {
            if (_connectionFactory is null)
                throw new InvalidOperationException("SecureLogService.Initialize must be called before WriteAsync.");

            // Default times
            var when = whenUtc ?? DateTime.UtcNow;
            var created = createdUtc ?? DateTime.UtcNow;

            // Serialize payload
            string payloadText = payload switch
            {
                null => "",
                string s => s,
                _ => JsonSerializer.Serialize(payload, _json)
            };

            // Load the v2 INSERT from secure store
            string sql = SecureEncryptedDataStore.GetString("Logs_Insert_V2.sql");
            if (string.IsNullOrWhiteSpace(sql))
                throw new InvalidOperationException("Logs_Insert_V2.sql not found in SecureEncryptedDataStore.");

            await using var conn = _connectionFactory();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;

            // REQUIRED parameters (match your script exactly)
            Add(cmd, "@WhenUtc", DbType.DateTime, when);
            Add(cmd, "@CreatedUtc", DbType.DateTime, created);

            // IMPORTANT: DB expects TEXT level with a CHECK constraint
            Add(cmd, "@Level", DbType.String, MapLevel(level));

            Add(cmd, "@Source", DbType.String, source ?? "App");
            Add(cmd, "@EventCode", DbType.String, string.IsNullOrWhiteSpace(eventCode) ? "EVENT_0" : eventCode);
            Add(cmd, "@SessionId", DbType.String, _sessionId);
            Add(cmd, "@MachineId", DbType.String, _machineId);
            Add(cmd, "@AppVersion", DbType.String, _appVersion);
            Add(cmd, "@IsCrash", DbType.Int32, isCrash ? 1 : 0);
            Add(cmd, "@Payload", DbType.String, payloadText);
            Add(cmd, "@PayloadFmt", DbType.String, string.IsNullOrWhiteSpace(payloadFmt) ? "json" : payloadFmt);
            Add(cmd, "@StackHash", DbType.String, stackHash ?? "");

            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        private static string MapLevel(LogSeverity level) => level switch
        {
            LogSeverity.Debug => "DEBUG",
            LogSeverity.Info => "INFO",
            LogSeverity.Warn => "WARN",
            LogSeverity.Error => "ERROR",
            LogSeverity.Critical => "FATAL",   // schema expects FATAL, not CRITICAL
            _ => "INFO"
        };

        private static void Add(SqliteCommand cmd, string name, DbType type, object? value)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.DbType = type;
            p.Value = value ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }
    }
}
