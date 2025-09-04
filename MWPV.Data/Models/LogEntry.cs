namespace MWPV.Data.Models;

/// <summary>
/// Represents a log entry persisted in the database.
/// </summary>
public sealed class LogEntry
{
    /// <summary>Primary key for the log entry.</summary>
    public long LogEntry_Key { get; set; }

    /// <summary>UTC timestamp when the event occurred.</summary>
    public DateTime TimestampUtc { get; set; }

    /// <summary>Event name or identifier.</summary>
    public string EventName { get; set; } = "";

    /// <summary>Structured metadata (JSON or text) describing the event.</summary>
    public string? Metadata { get; set; }

    /// <summary>Severity (Info, Warn, Error, etc.).</summary>
    public string Severity { get; set; } = "Info";
}
