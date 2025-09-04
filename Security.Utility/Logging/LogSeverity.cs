// Utilities/Logging/LogSeverity.cs
namespace Security.Utility.Logging
{
    /// <summary>
    /// Normalized severity levels for application logging.
    /// </summary>
    public enum LogSeverity
    {
        Trace = 5,     // Extremely verbose
        Debug = 10,    // Dev-only detail
        Info = 20,     // Normal operation
        Warn = 30,     // Unexpected but continuing
        Error = 40,    // Operation failed
        Critical = 50, // App/service at risk
        Fatal = 60     // Non-recoverable / crash
    }

    /// <summary>Optional helpers (no external deps).</summary>
    public static class LogSeverityExtensions
    {
        public static string ToShortTag(this LogSeverity s) => s switch
        {
            LogSeverity.Trace => "TRACE",
            LogSeverity.Debug => "DEBUG",
            LogSeverity.Info => "INFO",
            LogSeverity.Warn => "WARN",
            LogSeverity.Error => "ERROR",
            LogSeverity.Critical => "CRIT",
            LogSeverity.Fatal => "FATAL",
            _ => s.ToString().ToUpperInvariant()
        };

        public static bool IsAtLeast(this LogSeverity s, LogSeverity threshold) => s >= threshold;
    }
}
