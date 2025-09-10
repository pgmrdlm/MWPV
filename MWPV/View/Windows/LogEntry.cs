using System;

namespace MWPV.View.Windows
{
    /// <summary>
    /// Lightweight DTO the LogDetailsWindow binds to.
    /// </summary>
    public sealed class LogEntry
    {
        public string Id { get; set; } = "";
        public DateTime CreatedUtc { get; set; }
        public string Level { get; set; } = "";
        public string Source { get; set; } = "";
        public string EventCode { get; set; } = "";

        // DB no longer has Message; keep nullable for future use (bind-safe)
        public string? Message { get; set; }

        public string PayloadFmt { get; set; } = "none";
        public int PayloadSize { get; set; }
        public string? Payload { get; set; } // decoded text (json/text) or null
    }
}
