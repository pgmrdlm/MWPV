// Utilities/Helpers/ErrorHandler.cs
// Repo-free, pluggable sink ready, preserves existing public surface (InfoTitled/Abend overloads).

#nullable enable
using System;
using System.Text;
using System.Windows;                     // MessageBox
using Security.Utility.Logging;           // LogSeverity + extensions
using LogSeverity = Security.Utility.Logging.LogSeverity;
// Explicit alias to centralized SHA-256 helper (avoid clash with any legacy helper)
using HashSha256 = Security.Utility.Crypto.Hash.Sha256Common;

namespace Utilities.Helpers
{
    public enum ErrorSeverity { Info, Warning, Error, Critical }

    /// <summary>
    /// Centralized UI + diagnostic logging helper.
    /// - Shows MessageBoxes for user-visible info/errors.
    /// - Writes developer diagnostics to Debug output.
    /// - (Optional) Can forward events to a caller-provided sink (e.g., DB/file) without a hard dependency.
    /// </summary>
    public static class ErrorHandler
    {
        /// <summary>
        /// Optional async sink for forwarding log events (e.g., to DB/file). Assign at app startup if desired.
        /// Signature: (whenUtc, level, category, message, exType, exMessage, exStack, relatedFile, contentHashHex, source)
        /// </summary>
        public static Func<DateTime, LogSeverity, string, string,
                           string?, string?, string?,
                           string?, string?, string,
                           System.Threading.Tasks.Task>? LogSinkAsync
        { get; set; }

        // -------- Core logger --------
        public static void Log(
            ErrorSeverity severity,
            string category,
            string message,
            Exception? ex = null,
            string? relatedFile = null,
            string? contentHashHex = null,
            string source = "app")
        {
            var sev = MapLevel(severity);
            var now = DateTime.UtcNow;

            // Optional external sink (fire-and-forget)
            try
            {
                var sink = LogSinkAsync;
                if (sink != null)
                {
                    _ = sink(now, sev, category, message,
                             ex?.GetType().FullName,
                             ex?.Message,
                             ex?.StackTrace ?? ex?.ToString(),
                             relatedFile,
                             contentHashHex,
                             source);
                }
            }
            catch (Exception sinkEx)
            {
                _ = sinkEx;
            }
        }

        // -------- Convenience --------
        public static void Info(string category, string message) =>
            Log(ErrorSeverity.Info, category, message);

        public static void Warn(string category, string message, string? relatedFile = null) =>
            Log(ErrorSeverity.Warning, category, message, relatedFile: relatedFile);

        public static void Error(string category, string message, Exception? ex = null, string? relatedFile = null) =>
            Log(ErrorSeverity.Error, category, message, ex, relatedFile);

        public static void Critical(string category, string message, Exception? ex = null, string? relatedFile = null) =>
            Log(ErrorSeverity.Critical, category, message, ex, relatedFile, source: "critical");

        // -------- Primary UI helpers (UNAMBIGUOUS) --------
        /// <summary>Show info dialog + log at Info level with explicit stage/category.</summary>
        public static void InfoTitled(string title, string message, string stage)
        {
            try { MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information); } catch { }
            Info(stage, message);
        }

        /// <summary>Show error dialog + log at Critical level with explicit category.</summary>
        public static void Abend(string title, string category, string message, Exception? ex)
        {
            try
            {
                var body = ex == null ? message : $"{message}\n\n{ex}";
                MessageBox.Show(body, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
            Critical(category, message, ex);
        }

        // -------- Back-compat overloads (bind old call sites) --------
        public static void InfoTitled(string title, string message)
        {
            try { MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information); } catch { }
            Info("Info", message);
        }

        public static void InfoTitled(string title, string message, Enum stageEnum) =>
            InfoTitled(title, message, stageEnum.ToString());

        public static void Abend(Exception ex, string message, string stage) =>
            Abend("Error", stage, message, ex);

        public static void Abend(Exception ex, string message) =>
            Abend("Error", "Abend", message, ex);

        public static void Abend(Exception ex) =>
            Abend("Error", "Abend", ex.Message, ex);

        public static void Abend(Exception ex, string message, Enum stageEnum) =>
            Abend("Error", stageEnum.ToString(), message, ex);

        // -------- Helpers --------
        private static LogSeverity MapLevel(ErrorSeverity s) =>
            s switch
            {
                ErrorSeverity.Info => LogSeverity.Info,
                ErrorSeverity.Warning => LogSeverity.Warn,
                ErrorSeverity.Error => LogSeverity.Error,
                ErrorSeverity.Critical => LogSeverity.Critical,
                _ => LogSeverity.Info
            };

        /// <summary>Short SHA256 hex for identifiers/titles (12 hex chars).</summary>
        public static string ShortHash(string s)
        {
            if (string.IsNullOrEmpty(s)) return "0";
            return HashSha256.ShortHex(s, takeBytes: 6); // centralized helper
        }
    }
}
