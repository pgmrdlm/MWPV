// File: Models/LogMessageTemplate.cs
//
// Represents a single log message template row.
// Non-sensitive, static metadata loaded into memory at runtime.
//
// Used for:
// - Building log rows after compares
// - Driving UI log rendering
//


namespace MWPV.Models
{
    public sealed class LogMessageTemplate
    {
        public string UpdateForm { get; init; } = string.Empty;
        public int Seq { get; init; }
        public string LogMessage { get; init; } = string.Empty;
        public bool Active { get; init; }
    }
}
