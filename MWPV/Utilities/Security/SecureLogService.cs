using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

// Use the canonical enum from your logging namespace to avoid ambiguity
using LogSeverity = Security.Utility.Logging.LogSeverity;

namespace Security.Utility
{
    /// <summary>
    /// Standalone secure-log PREPARATION (no SQL, no app references).
    /// Prepares payload and forwards a V3 envelope to a host-provided insert delegate.
    /// </summary>
    public static class SecureLogService
    {
        // Host-provided insert callback (envelope -> inserted id)
        private static Func<LogWriteEnvelopeV3, long>? _insertV3;

        private static Func<SqliteConnection>? _connectionFactory;
        private static string _appVersion = "unknown";
        private static string _machineId = Environment.MachineName;
        private static readonly JsonSerializerOptions _json = new() { WriteIndented = false };

        /// <summary>Call once at startup from the host app.</summary>
        public static void Initialize(
            Func<SqliteConnection> connectionFactory,
            string appVersion,
            string machineId,
            Func<LogWriteEnvelopeV3, long> insertV3Callback)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _appVersion = string.IsNullOrWhiteSpace(appVersion) ? "unknown" : appVersion;
            _machineId = string.IsNullOrWhiteSpace(machineId) ? Environment.MachineName : machineId;
            _insertV3 = insertV3Callback ?? throw new ArgumentNullException(nameof(insertV3Callback));
        }

        /// <summary>
        /// Prepare-and-write via host insert delegate (V3). No SQL here.
        /// </summary>
        public static async Task<long> WriteAsync(
            LogSeverity level,
            object? payload,
            string eventCode,
            string source,
            string message,
            string? payloadFmt = null)
        {
            if (_insertV3 is null)
                throw new InvalidOperationException("SecureLogService.Initialize must be called with an insert callback.");
            if (_connectionFactory is null)
                throw new InvalidOperationException("SecureLogService.Initialize must be called before WriteAsync.");

            // Prepare payload bytes + format
            byte[]? bytes;
            string fmt;

            switch (payload)
            {
                case null:
                    bytes = null; fmt = "none";
                    break;
                case byte[] b:
                    bytes = b; fmt = string.IsNullOrWhiteSpace(payloadFmt) ? "binary" : payloadFmt!;
                    break;
                case string s:
                    bytes = Encoding.UTF8.GetBytes(s);
                    fmt = string.IsNullOrWhiteSpace(payloadFmt) ? "json" : payloadFmt!;
                    break;
                default:
                    var json = JsonSerializer.Serialize(payload, _json);
                    bytes = Encoding.UTF8.GetBytes(json);
                    fmt = string.IsNullOrWhiteSpace(payloadFmt) ? "json" : payloadFmt!;
                    break;
            }

            // If/when you add encryption:
            // - Encrypt 'bytes' with LogPayloadKey
            // - Set fmt = "aes-gcm"
            // - Size = ciphertext length

            var env = new LogWriteEnvelopeV3
            {
                Level = MapLevel(level),
                Source = string.IsNullOrWhiteSpace(source) ? "App" : source,
                EventCode = string.IsNullOrWhiteSpace(eventCode) ? "EVENT_0" : eventCode,
                Message = message ?? "",
                Payload = bytes,
                PayloadFmt = fmt,
                PayloadSize = bytes?.Length ?? 0,
                CreatedUtc = DateTime.UtcNow.ToString("o"),
                AppVersion = _appVersion
            };

            // Delegate to host’s single writer
            return await Task.Run(() => _insertV3!(env)).ConfigureAwait(false);
        }

        private static string MapLevel(LogSeverity level) => level switch
        {
            LogSeverity.Debug => "DEBUG",
            LogSeverity.Info => "INFO",
            LogSeverity.Warn => "WARN",
            LogSeverity.Error => "ERROR",
            LogSeverity.Critical => "FATAL",
            _ => "INFO"
        };
    }

    /// <summary>
    /// Standalone V3 envelope (no SQL knowledge, no app references).
    /// Host maps this to actual SQL params.
    /// </summary>
    public sealed class LogWriteEnvelopeV3
    {
        public string Level { get; set; } = "";
        public string Source { get; set; } = "";
        public string EventCode { get; set; } = "";
        public string Message { get; set; } = "";
        public byte[]? Payload { get; set; }
        public string PayloadFmt { get; set; } = "none";
        public int PayloadSize { get; set; } = 0;
        public string CreatedUtc { get; set; } = "";
        public string AppVersion { get; set; } = "";
    }
}
