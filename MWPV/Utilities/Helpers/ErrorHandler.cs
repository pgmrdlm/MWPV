// Utilities/Helpers/ErrorHandler.cs — full file (modern + compat overloads)
using System;
using System.Security.Cryptography;
using System.Text;
using System.Windows;                     // MessageBox
using MWPV.Services;                      // LogRepository
using Security.Utility.Logging;           // LogSeverity + extensions
using LogSeverity = Security.Utility.Logging.LogSeverity;


namespace Utilities.Helpers
{
    public enum ErrorSeverity { Info, Warning, Error, Critical }

    public static class ErrorHandler
    {
        private static LogRepository? _repo;
        public static void Initialize(LogRepository repo) => _repo = repo;

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

            // dev visibility
            try
            {
                global::System.Diagnostics.Debug.WriteLine($"[{DateTime.UtcNow:O}] [{sev.ToShortTag()}] {category}: {message}");
                if (ex != null) global::System.Diagnostics.Debug.WriteLine(ex.ToString());
            }
            catch { }

            // repo sink (fire-and-forget)
            try
            {
                if (_repo != null)
                {
                    _ = _repo.LogAsync(
                        whenUtc: DateTime.UtcNow,
                        severity: sev,
                        category: category,
                        message: message,
                        relatedFile: relatedFile,
                        exType: ex?.GetType().FullName,
                        exMessage: ex?.Message,
                        exStack: ex?.StackTrace ?? ex?.ToString(),
                        contentHashHex: contentHashHex,
                        source: source
                    );
                }
            }
            catch (Exception repoEx)
            {
                try { global::System.Diagnostics.Debug.WriteLine($"[LOGFAIL] {repoEx}"); } catch { }
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

        // Canonical 3-string overload (keep ONE ONLY to avoid ambiguity)
        public static void InfoTitled(string title, string message, string stage)
        {
            try { MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information); } catch { }
            Info(stage, message);
        }

        // UI error with explicit category (stage). Distinct signature.
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

        // Old pattern: InfoTitled(title, message) with no stage.
        public static void InfoTitled(string title, string message)
        {
            try { MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information); } catch { }
            Info("Info", message);
        }

        // Old pattern: InfoTitled(title, message, Enum stageEnum) — e.g., EarlyFailType
        public static void InfoTitled(string title, string message, Enum stageEnum) =>
            InfoTitled(title, message, stageEnum.ToString());

        // Old pattern: Abend(ex, message, stage)
        public static void Abend(Exception ex, string message, string stage) =>
            Abend("Error", stage, message, ex);

        // Old pattern: Abend(ex, message)
        public static void Abend(Exception ex, string message) =>
            Abend("Error", "Abend", message, ex);

        // Old pattern: Abend(ex)
        public static void Abend(Exception ex) =>
            Abend("Error", "Abend", ex.Message, ex);

        // Old pattern: Abend(ex, message, Enum stageEnum)
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

        /// <summary>Short SHA256 hex for identifiers/titles.</summary>
        public static string ShortHash(string s)
        {
            if (string.IsNullOrEmpty(s)) return "0";
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
            var sb = new StringBuilder(12);
            for (int i = 0; i < 6 && i < bytes.Length; i++)
                sb.Append(bytes[i].ToString("x2"));
            return sb.ToString();
        }
    }
}
