// Utilities/Helpers/ErrorHandler.cs
// Centralized user-visible dialogs + encrypted diagnostic logging via SecureLogService.
// - Works before Configure(): shows dialogs; logging uses SecureLogService (which is safe to call even if not inited).
// - After Configure(...): includes app version/session id details in payloads.
// - Adds InfoTitled(title, message, ...) and WarnTitled(title, message, ...) to keep popup titles consistent.

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using MWPV.Services;                              // LogRepository (kept in signature for compatibility)
using Utilities.Security;                          // SecureLogService
using Utilities.Logging;                           // LogSeverity, LogEventIds
using WpfMessageBox = System.Windows.MessageBox;   // Alias for clarity
using WpfImage = System.Windows.MessageBoxImage;
using WpfButtons = System.Windows.MessageBoxButton;

namespace Utilities.Helpers
{
    public enum ErrorSeverity { Info, Warning, Error, Critical }

    public static class ErrorHandler
    {
        // ===== Optional dependencies (injected post-login) =====
        private static LogRepository? _logs;                // no longer used for writes; kept for compatibility
        private static Func<string>? _sessionIdProvider;
        private static string _appVersion = "unknown";

        /// <summary>Call once after DB/key setup succeeds.</summary>
        public static void Configure(LogRepository logs, Func<string>? sessionIdProvider = null, string? appVersion = null)
        {
            _logs = logs; // retained for compatibility with existing Configure() call sites
            _sessionIdProvider = sessionIdProvider;
            if (!string.IsNullOrWhiteSpace(appVersion))
                _appVersion = appVersion!;
        }

        // ===== Return model =====
        public readonly struct ErrorResult
        {
            public string ActivityId { get; init; }
            public System.Windows.MessageBoxResult UserChoice { get; init; }
            public ErrorSeverity Severity { get; init; }
        }

        // ===== Public API =====

        /// <summary>
        /// Critical/error dialog with encrypted logging (best-effort; never throws).
        /// </summary>
        public static ErrorResult Abend(
            Exception ex,
            string contextMessage = "A critical error has occurred",
            string stage = "unspecified",
            ErrorSeverity severity = ErrorSeverity.Error,
            WpfButtons buttons = WpfButtons.OK,
            WpfImage icon = WpfImage.Stop,
            bool log = true,
            [CallerMemberName] string member = "",
            [CallerFilePath] string file = "",
            [CallerLineNumber] int line = 0)
        {
            ex ??= new Exception("Unknown exception");
            string activityId = Guid.NewGuid().ToString("N");
            string fileShort = SafeFileName(file);
            string location = $"{fileShort}.{member}():{line}";
            string errorType = ex.GetType().Name;

            // ---- User-facing message (safe) ----
            var sb = new StringBuilder()
                .AppendLine(contextMessage)
                .AppendLine($"Type: {errorType}")
                .AppendLine($"Location: {location}")
                .AppendLine($"Stage: {stage}")
                .AppendLine($"Error ID: {activityId}");

            if (!string.IsNullOrWhiteSpace(ex.Message))
                sb.AppendLine().AppendLine($"Details: {ex.Message}");

#if DEBUG
            if (!string.IsNullOrWhiteSpace(ex.StackTrace))
                sb.AppendLine().AppendLine("Stack Trace:").AppendLine(ex.StackTrace);
#endif
            var result = WpfMessageBox.Show(
                sb.ToString(),
                severity >= ErrorSeverity.Critical ? "Fatal Error" : "Application Error",
                buttons,
                icon
            );

            // ---- Encrypted logging (best-effort; never throws) ----
            if (log)
            {
                try
                {
                    var payload = new
                    {
                        message = Redact(contextMessage),
                        stage,
                        severity = severity.ToString(),
                        caller = new { member, file, line, location },
                        environment = new
                        {
                            utc = DateTime.UtcNow,
                            machine = Environment.MachineName,
                            processId = Environment.ProcessId,
                            os = Environment.OSVersion?.ToString(),
                            appVersion = _appVersion,
                            sessionId = _sessionIdProvider?.Invoke()
                        },
                        activityId
                    };

                    // Route via SecureLogService with numeric EventId
                    _ = SecureLogService.WriteAsync(
                        level: MapSeverity(severity),
                        payload: payload,
                        eventId: (int)LogEventIds.Abend,
                        source: location,
                        ex: ex,
                        isCrash: severity >= ErrorSeverity.Critical
                    );
                }
                catch
                {
                    // swallow: handler must never throw
                }
            }

            return new ErrorResult { ActivityId = activityId, UserChoice = result, Severity = severity };
        }

        /// <summary>Info dialog (title = "Info"). Logs at Info.</summary>
        public static void Info(
            string message,
            string stage = "unspecified",
            [CallerMemberName] string member = "",
            [CallerFilePath] string file = "",
            [CallerLineNumber] int line = 0)
        {
            InfoTitled("Info", message, stage, member, file, line);
        }

        /// <summary>Info dialog with custom title. Logs at Info.</summary>
        public static void InfoTitled(
            string title,
            string message,
            string stage = "unspecified",
            [CallerMemberName] string member = "",
            [CallerFilePath] string file = "",
            [CallerLineNumber] int line = 0)
        {
            WpfMessageBox.Show(message, string.IsNullOrWhiteSpace(title) ? "Info" : title, WpfButtons.OK, WpfImage.Information);

            try
            {
                var location = $"{SafeFileName(file)}.{member}():{line}";
                var payload = new
                {
                    title = Redact(title),
                    message = Redact(message),
                    stage,
                    severity = ErrorSeverity.Info.ToString(),
                    caller = new { member, file, line, location },
                    environment = new
                    {
                        utc = DateTime.UtcNow,
                        machine = Environment.MachineName,
                        processId = Environment.ProcessId,
                        os = Environment.OSVersion?.ToString(),
                        appVersion = _appVersion,
                        sessionId = _sessionIdProvider?.Invoke()
                    }
                };

                _ = SecureLogService.WriteAsync(
                    level: LogSeverity.Info,
                    payload: payload,
                    eventId: null,
                    source: location
                );
            }
            catch { /* never throw */ }
        }

        /// <summary>Warning dialog with custom title. Logs at Warn.</summary>
        public static void WarnTitled(
            string title,
            string message,
            string stage = "unspecified",
            [CallerMemberName] string member = "",
            [CallerFilePath] string file = "",
            [CallerLineNumber] int line = 0)
        {
            WpfMessageBox.Show(message, string.IsNullOrWhiteSpace(title) ? "Warning" : title, WpfButtons.OK, WpfImage.Warning);

            try
            {
                var location = $"{SafeFileName(file)}.{member}():{line}";
                var payload = new
                {
                    title = Redact(title),
                    message = Redact(message),
                    stage,
                    severity = ErrorSeverity.Warning.ToString(),
                    caller = new { member, file, line, location },
                    environment = new
                    {
                        utc = DateTime.UtcNow,
                        machine = Environment.MachineName,
                        processId = Environment.ProcessId,
                        os = Environment.OSVersion?.ToString(),
                        appVersion = _appVersion,
                        sessionId = _sessionIdProvider?.Invoke()
                    }
                };

                _ = SecureLogService.WriteAsync(
                    level: LogSeverity.Warn,
                    payload: payload,
                    eventId: null,
                    source: location
                );
            }
            catch { /* never throw */ }
        }

        /// <summary>Try wrapper for actions. Returns false on exception.</summary>
        public static bool Try(
            Action action,
            string contextMessage = "Operation failed",
            string stage = "unspecified",
            ErrorSeverity severity = ErrorSeverity.Error,
            bool rethrow = false,
            [CallerMemberName] string member = "",
            [CallerFilePath] string file = "",
            [CallerLineNumber] int line = 0)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception ex)
            {
                Abend(ex, contextMessage, stage, severity, WpfButtons.OK, IconFor(severity),
                      log: true, member: member, file: file, line: line);
                if (rethrow) throw;
                return false;
            }
        }

        /// <summary>Try wrapper for funcs. Returns default(T) on exception.</summary>
        public static T? Try<T>(
            Func<T> func,
            string contextMessage = "Operation failed",
            string stage = "unspecified",
            ErrorSeverity severity = ErrorSeverity.Error,
            bool rethrow = false,
            [CallerMemberName] string member = "",
            [CallerFilePath] string file = "",
            [CallerLineNumber] int line = 0)
        {
            try
            {
                return func();
            }
            catch (Exception ex)
            {
                Abend(ex, contextMessage, stage, severity, WpfButtons.OK, IconFor(severity),
                      log: true, member: member, file: file, line: line);
                if (rethrow) throw;
                return default;
            }
        }

        /// <summary>Register global handlers so unhandled exceptions go through here.</summary>
        public static void RegisterGlobalHandlers(System.Windows.Application app)
        {
            if (app != null)
            {
                app.DispatcherUnhandledException += (s, e) =>
                {
                    try
                    {
                        Abend(e.Exception, "Unhandled UI thread exception", stage: "dispatcher",
                              severity: ErrorSeverity.Critical, buttons: WpfButtons.OK, icon: WpfImage.Error, log: true);
                    }
                    finally { e.Handled = true; }
                };
            }

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception ?? new Exception("Unknown unhandled exception");
                Abend(ex, "Unhandled domain exception", stage: "appdomain",
                      severity: ErrorSeverity.Critical, buttons: WpfButtons.OK, icon: WpfImage.Error, log: true);
            };

            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                try
                {
                    Abend(e.Exception, "Unobserved task exception", stage: "taskscheduler",
                          severity: ErrorSeverity.Error, buttons: WpfButtons.OK, icon: WpfImage.Warning, log: true);
                }
                finally { e.SetObserved(); }
            };
        }

        // ===== Helpers =====

        private static string SafeFileName(string path)
            => string.IsNullOrWhiteSpace(path) ? "UnknownFile" : Path.GetFileNameWithoutExtension(path);

        private static WpfImage IconFor(ErrorSeverity severity) =>
            severity switch
            {
                ErrorSeverity.Info => WpfImage.Information,
                ErrorSeverity.Warning => WpfImage.Warning,
                ErrorSeverity.Error => WpfImage.Error,
                ErrorSeverity.Critical => WpfImage.Stop,
                _ => WpfImage.Error
            };

        private static LogSeverity MapSeverity(ErrorSeverity s) =>
            s switch
            {
                ErrorSeverity.Info => LogSeverity.Info,
                ErrorSeverity.Warning => LogSeverity.Warn,
                ErrorSeverity.Error => LogSeverity.Error,
                ErrorSeverity.Critical => LogSeverity.Critical,
                _ => LogSeverity.Error
            };

        private static string ShortHash(string s)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            // 8 hex chars is enough to bucket similar stacks without leaking too much
            return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(s))).Substring(0, 8);
        }

        // Very light PII/secret redaction (best-effort)
        private static string? Redact(string? s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            // match: password: foo, key=bar, token abc, secret:xxx, etc.
            return Regex.Replace(
                s,
                @"(?i)(password|pass|pw|key|token|secret)\s*([:=]\s*|\s+)\S+",
                m => m.Groups[1].Value + ": [REDACTED]"
            );
        }
    }
}
