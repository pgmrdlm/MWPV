using System;
using System.Globalization;

namespace MWPV.Models
{
    /// <summary>
    /// POCO representing a row from the Logs table (payload not included).
    /// Suitable for binding to a DataGrid.
    /// </summary>
    public class Logs
    {
        public long Id { get; set; }

        // Raw UTC timestamps as stored in SQLite (TEXT)
        public string WhenUtc { get; set; } = "";
        public string CreatedUtc { get; set; } = "";

        // Core fields
        public string Level { get; set; } = "";        // TRACE/DEBUG/INFO/WARN/ERROR/FATAL
        public string? Source { get; set; }
        public string? EventCode { get; set; }
        public string SessionId { get; set; } = "";
        public string MachineId { get; set; } = "";
        public string AppVersion { get; set; } = "";
        public bool IsCrash { get; set; }

        // Payload metadata only (blob not surfaced here)
        public string? PayloadFmt { get; set; }         // e.g., "gcm-json-v1"
        public int PayloadVer { get; set; } = 1;
        public int KeySetVersion { get; set; } = 1;

        public string? StackHash { get; set; }

        // Optional: if your SELECT includes length(Payload) AS PayloadSize
        public int? PayloadSize { get; set; }

        // Convenience (parsed timestamps)
        public DateTimeOffset? WhenParsed => ParseUtc(WhenUtc);
        public DateTimeOffset? CreatedParsed => ParseUtc(CreatedUtc);

        // Bind-friendly local strings (empty when not parseable)
        public string WhenLocal => WhenParsed?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "";
        public string CreatedLocal => CreatedParsed?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "";

        private static DateTimeOffset? ParseUtc(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;

            // Accept common ISO/SQLite formats; assume/adjust to UTC
            if (DateTimeOffset.TryParse(
                    s, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var dto))
            {
                return dto;
            }

            if (DateTime.TryParse(
                    s, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var dt))
            {
                return new DateTimeOffset(dt, TimeSpan.Zero);
            }

            return null;
        }
    }
}
