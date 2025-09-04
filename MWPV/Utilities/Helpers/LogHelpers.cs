namespace Utilities.Helpers
{
    public static class LogHelpers
    {
        /// <summary>
        /// Map any level name to the exact values allowed by the Logs CHECK constraint.
        /// Accepts things like "Trace", "CRITICAL", "Fatal", "Warning" and normalizes.
        /// </summary>
        public static string MapLevelName(string levelName)
        {
            if (string.IsNullOrWhiteSpace(levelName)) return "INFO";
            switch (levelName.Trim().ToUpperInvariant())
            {
                case "TRACE": return "TRACE";
                case "DEBUG": return "DEBUG";
                case "INFO": return "INFO";
                case "WARN":
                case "WARNING": return "WARN";
                case "ERROR": return "ERROR";
                case "FATAL":
                case "CRITICAL": return "FATAL";
                default: return "INFO";
            }
        }

        /// <summary>
        /// Logs.CreatedUtc wants an INTEGER (unix seconds).
        /// </summary>
        public static long NowUnixSeconds() =>
            System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}
