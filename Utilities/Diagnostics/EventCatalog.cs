// Utilities/Diagnostics/EventCatalog.cs
// Canonical .elog shape + helpers (normalize, (de)serialize, dedupe).

using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Utilities.Diagnostics
{
    public static class EventCatalog
    {
        public const string CurrentVersion = "elog-v1";

        private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.General)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        // ---- .elog V1 shape ----
        // Record class so we can use `with { ... }` for normalization.
        public sealed record class ElogV1
        {
            public string Version { get; init; } = CurrentVersion; // e.g., "elog-v1"
            public string EventCode { get; init; } = "";           // e.g., "EARLY_LOGIN_FAILURE"
            public DateTime OccurredUtc { get; init; } = DateTime.UtcNow;
            public string Source { get; init; } = "";              // e.g., "SetupPasswordAndKeyFile"
            public string? SessionId { get; init; }                // optional
            public Guid? LogGuid { get; init; }                    // preferred dedupe key
            public string Payload { get; init; } = "";             // JSON string
            public string? Etag { get; init; }                     // fallback dedupe hash
        }

        // Create a new v1 row (convenience).
        public static ElogV1 MakeV1(
            string eventCode,
            string source,
            string payloadJson,
            Guid? logGuid = null,
            string? sessionId = null,
            DateTime? occurredUtc = null)
        {
            var e = new ElogV1
            {
                Version = CurrentVersion,
                EventCode = eventCode ?? "",
                Source = source ?? "",
                Payload = payloadJson ?? "{}",
                LogGuid = logGuid,
                SessionId = sessionId,
                OccurredUtc = occurredUtc?.ToUniversalTime() ?? DateTime.UtcNow
            };
            return Normalize(e);
        }

        // Ensure Version, OccurredUtc (UTC), and Etag are set.
        public static ElogV1 Normalize(ElogV1 e)
        {
            var utc = e.OccurredUtc.Kind == DateTimeKind.Utc ? e.OccurredUtc : e.OccurredUtc.ToUniversalTime();
            var ver = string.IsNullOrWhiteSpace(e.Version) ? CurrentVersion : e.Version;
            var etag = string.IsNullOrWhiteSpace(e.Etag) ? ComputeEtag(e) : e.Etag;

            // `with` requires ElogV1 to be a record (this fixes CS8858).
            return e with { OccurredUtc = utc, Version = ver, Etag = etag };
        }

        // Serialize to json (one line).
        public static string ToJson(ElogV1 e)
        {
            e = Normalize(e);
            return JsonSerializer.Serialize(e, JsonOpts);
        }

        // Try parse from json.
        public static bool TryParse(string json, out ElogV1? e)
        {
            try
            {
                e = JsonSerializer.Deserialize<ElogV1>(json, JsonOpts);
                if (e is null) return false;
                e = Normalize(e);
                return true;
            }
            catch
            {
                e = null;
                return false;
            }
        }

        // Preferred dedupe: exact LogGuid match; else ETag match.
        public static bool IsProbableDuplicate(ElogV1 a, ElogV1 b)
        {
            if (a.LogGuid.HasValue && b.LogGuid.HasValue)
                return a.LogGuid.Value == b.LogGuid.Value;

            var ea = string.IsNullOrWhiteSpace(a.Etag) ? ComputeEtag(a) : a.Etag!;
            var eb = string.IsNullOrWhiteSpace(b.Etag) ? ComputeEtag(b) : b.Etag!;
            return string.Equals(ea, eb, StringComparison.Ordinal);
        }

        public static string ComputeEtag(ElogV1 e)
        {
            // Keep it stable but not overly revealing: hash eventCode + source + payload
            var raw = $"{e.EventCode}\n{e.Source}\n{e.Payload}";
            return ShortHash(raw, 16); // 16 hex chars is plenty for bucketing
        }

        // Utility: short SHA256 hex.
        private static string ShortHash(string input, int hexLength)
        {
            var bytes = Encoding.UTF8.GetBytes(input ?? string.Empty);
            var hash = SHA256.HashData(bytes);
            var hex = Convert.ToHexString(hash);
            return hexLength > 0 && hexLength < hex.Length ? hex[..hexLength] : hex;
        }
    }
}
