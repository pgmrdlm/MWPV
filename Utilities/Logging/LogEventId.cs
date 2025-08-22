namespace Utilities.Logging
{
    /// <summary>
    /// Same values as LogEventId enum, but as constants on a class.
    /// Use either this or the enum, not both.
    /// </summary>
    public static class LogEventIds
    {
        // App lifecycle
        public const int AppStart = 1000;
        public const int AppShutdown = 1090;

        // Database
        public const int DbOpenAttempt = 1100;
        public const int DbOpenSucceeded = 1101;
        public const int DbOpenFailed = 1199;

        // Categories
        public const int CategoryAdd = 2100;
        public const int CategoryDuplicate = 2101;

        // Export / Backup / Restore
        public const int ExportStart = 3000;
        public const int ExportSucceeded = 3001;
        public const int ExportFailed = 3099;

        // Errors / Abends
        public const int Abend = 9000;
    }
}
