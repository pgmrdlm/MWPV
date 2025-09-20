using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Security.Utility;   // for JsonCore

namespace MWPV.Utilities.Json
{
    /// <summary>
    /// Central JSON facade with tuned options and domain DTOs.
    /// </summary>
    public static class AppJson
    {
        // ---- Generic helpers ----
        public static string Serialize<T>(T value, bool pretty = false) =>
            JsonSerializer.Serialize(value, pretty ? JsonCore.Pretty : JsonCore.Default);

        public static T? Deserialize<T>(string json) =>
            JsonSerializer.Deserialize<T>(json, JsonCore.Default);

        public static bool TryDeserialize<T>(string json, out T? value)
        {
            try
            {
                value = JsonSerializer.Deserialize<T>(json, JsonCore.Default);
                return true;
            }
            catch
            {
                value = default;
                return false;
            }
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
            JsonSerializer.Serialize(dto, pretty ? JsonCore.Pretty : JsonCore.Default);

        public static LogPayloadDto? DeserializeLogPayload(string json) =>
            JsonSerializer.Deserialize<LogPayloadDto>(json, JsonCore.Default);
    }
}
