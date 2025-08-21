namespace Utilities.Logging
{
    /// <summary>
    /// Normalized severity levels for application logging.
    /// </summary>
    public enum LogSeverity
    {
        Debug = 10, // Dev-only detail
        Info = 20, // Normal operation
        Warn = 30, // Unexpected but continuing
        Error = 40, // Operation failed
        Critical = 50  // App/service at risk
    }
}