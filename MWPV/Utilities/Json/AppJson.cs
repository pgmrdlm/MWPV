// File: MWPV/Utilities/Json/AppJson.cs
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MWPV.Utilities.Json
{
    /// <summary>
    /// Central JSON facade. One place to tune serializer options and expose
    /// typed helpers for domain payloads (starting with Logs).
    /// </summary>
    public static class AppJson
    {
        // ---- Serializer option profiles ----
        private static readonly JsonSerializerOptions DefaultOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.Strict,
            AllowTrailingCommas = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
            }
        };

        private static readonly JsonSerializerOptions PrettyOptions = new(DefaultOptions)
        {
            WriteIndented = true
        };

        // If you need DB/persist-specific tweaks later, change here
        private static readonly JsonSerializerOptions PersistOptions = new(DefaultOptions)
        {
            // e.g., custom date converters if required in future
        };

        // ---- Generic helpers ----
        public static string Serialize<T>(T value, bool pretty = false) =>
            JsonSerializer.Serialize(value, pretty ? PrettyOptions : DefaultOptions);

        public static T? Deserialize<T>(string json) =>
            JsonSerializer.Deserialize<T>(json, DefaultOptions);

        public static bool TryDeserialize<T>(string json, out T? value)
        {
            try { value = JsonSerializer.Deserialize<T>(json, DefaultOptions); return true; }
            catch { value = default; return false; }
        }

        // ===================== Domain: Logs =====================

        /// <summary>
        /// Canonical payload shape for log entries written to Logs_* tables.
        /// Extend cautiously to keep backward compatibility.
        /// </summary>
        public sealed class LogPayloadDto
        {
            public string? Message { get; set; }
            public string? Source { get; set; }     // e.g., "Login", "Auth", "EarlyIngest"
            public string? EventCode { get; set; }  // e.g., "LOGIN", "INVALID_PASSWORD"
            public string? Context { get; set; }    // optional JSON/text blob
            public DateTime? OccurredUtc { get; set; }
            public string? User { get; set; }       // optional actor/subject
        }

        public static string SerializeLogPayload(LogPayloadDto dto, bool pretty = false) =>
            JsonSerializer.Serialize(dto, pretty ? PrettyOptions : PersistOptions);

        public static LogPayloadDto? DeserializeLogPayload(string json) =>
            JsonSerializer.Deserialize<LogPayloadDto>(json, DefaultOptions);

        public static bool TryDeserializeLogPayload(string json, out LogPayloadDto? dto)
        {
            try { dto = JsonSerializer.Deserialize<LogPayloadDto>(json, DefaultOptions); return true; }
            catch { dto = null; return false; }
        }

        // ---- Encryption stubs (wire to Security.Utility when ready) ----
        public static string SerializeEncryptedLogPayload(LogPayloadDto dto)
        {
            var plaintext = SerializeLogPayload(dto);
            // TODO: encrypt via Security.Utility (LogPayloadKey)
            return plaintext; // placeholder
        }

        public static LogPayloadDto? DeserializeEncryptedLogPayload(string ciphertext)
        {
            // TODO: decrypt via Security.Utility
            return DeserializeLogPayload(ciphertext);
        }
    }
}
