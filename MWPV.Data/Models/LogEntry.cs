using System;

namespace MWPV.Data.Models;

public sealed class LogEntry
{
    public long LogId { get; set; }                  // PK
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = "";          // Info / Warn / Error
    public string Event { get; set; } = "";
    public string? Metadata { get; set; }            // JSON payload
}
