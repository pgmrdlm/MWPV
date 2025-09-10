// File: MWPV/Utilities/Json/AppJson.cs
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MWPV.Utilities.Json
{
    /// <summary>
    /// Central JSON facade with tuned options and domain DTOs.
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

        // If you later need persist-specific tweaks, fork from DefaultOptions here.
        private static readonly JsonSerializerOptions PersistOptions = new(DefaultOptions);

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
        /// Canonical payload shape for log entries stored in Logs.
        /// Forward-only: keep original structured data in Context.
        /// </summary>
        public sealed class LogPayloadDto
        {
            public string? Message { get; set; }
            public string? Source { get; set; }     // e.g., "EarlyIngest", "CategoryService"
            public string? EventCode { get; set; }  // e.g., "EARLY_FAIL", "CATEGORY_INSERTED"
            public DateTime? OccurredUtc { get; set; }

            // Keep original details as structured JSON (if any)
            public JsonElement? Context { get; set; }
        }

        public static string SerializeLogPayload(LogPayloadDto dto, bool pretty = false) =>
            JsonSerializer.Serialize(dto, pretty ? PrettyOptions : PersistOptions);

        public static LogPayloadDto? DeserializeLogPayload(string json) =>
            JsonSerializer.Deserialize<LogPayloadDto>(json, DefaultOptions);
    }
}
