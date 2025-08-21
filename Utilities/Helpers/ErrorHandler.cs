// Utilities/Helpers/ErrorHandler.cs
// Centralized user-visible dialogs + encrypted diagnostic logging via LogRepository.
// - Safe before Configure(): shows dialogs, skips logging if _logs is null.
// - After Configure(...): writes encrypted logs too.
// - Flood protection for background task popups (UnobservedTaskException).

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using MWPV.Services;                              // LogRepository, LogLevel
using WpfMessageBox = System.Windows.MessageBox;  // alias for clarity
using WpfImage = System.Windows.MessageBoxImage;
using WpfButtons = System.Windows.MessageBoxButton;

namespace Utilities.Helpers
{
    public enum ErrorSeverity { Info, Warning, Error, Critical }

    public static class ErrorHandler
    {
        // ===== Dependencies (injected post-login) =====
        private static LogRepository? _logs;
        private static Func<string>? _sessionIdProvider;
        private static string _appVersion = "unknown";

        // register-once guard
        private static bool _handlersRegistered;

        // background popup flood protection
        private static readonly object _bgLock = new();
        private static DateTime _bgLastShownUtc;
        private static bool _bgPopupVisible;
        private static readonly TimeSpan _bgMinInterval = TimeSpan.FromSeconds(5);

        /// <summary>Call once after DB/key setup succeeds.</summary>
        public static void Configure(LogRepository logs, Func<string>? sessionIdProvider = null, string? appVersion = null)
        {
            _logs = logs ?? throw new ArgumentNullException(nameof(logs));
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
        /// Critical/error dialog with optional encrypted logging.
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

            // ---- Encrypted logging (best-effort) ----
            if (log && _logs is not null)
            {
                try
                {
                    var payload = new
                    {
                        message = Redact(contextMessage),
                        stage,
                        severity = severity.ToString(),
                        caller = new { member, file, line, location },
                        exception = new
                        {
                            type = errorType,
                            message = Redact(ex.Message),
                            stackTrace = ex.StackTrace
                        },
                        environment = new
                        {
                            utc = DateTime.UtcNow,
                            machine = Environment.MachineName,
                            processId = Environment.ProcessId,
                            os = Environment.OSVersion?.ToString(),
                            appVersion = _appVersion
                        },
                        activityId
                    };

                    string eventCode = $"ABEND_{stage}".ToUpperInvariant();
                    string? sessionId = _sessionIdProvider?.Invoke();
                    string stackHash = ShortHash(ex.ToString() ?? $"{errorType}:{location}");

                    _ = _logs.LogAsync(
                        level: MapLevel(severity),
                        source: location,
                        eventCode: eventCode,
                        payloadObject: payload,
                        isCrash: severity >= ErrorSeverity.Critical,
                        sessionId: sessionId,
                        stackHash: stackHash
                    );
                }
                catch
                {
                    // never throw
                }
            }

            return new ErrorResult { ActivityId = activityId, UserChoice = result, Severity = severity };
        }

        /// <summary>Info dialog (title = "Info"). Logs at Info if configured.</summary>
        public static void Info(
            string message,
            string stage = "unspecified",
            [CallerMemberName] string member = "",
            [CallerFilePath] string file = "",
            [CallerLineNumber] int line = 0)
            => InfoTitled("Info", message, stage, member, file, line);

        /// <summary>Info dialog with custom title. Logs at Info if configured.</summary>
        public static void InfoTitled(
            string title,
            string message,
            string stage = "unspecified",
            [CallerMemberName] string member = "",
            [CallerFilePath] string file = "",
            [CallerLineNumber] int line = 0)
        {
            WpfMessageBox.Show(message, string.IsNullOrWhiteSpace(title) ? "Info" : title, WpfButtons.OK, WpfImage.Information);

            if (_logs is null) return;
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
                        appVersion = _appVersion
                    }
                };

                _ = _logs.LogAsync(
                    level: LogLevel.Info,
                    source: location,
                    eventCode: "INFO",
                    payloadObject: payload,
                    isCrash: false,
                    sessionId: _sessionIdProvider?.Invoke(),
                    stackHash: null
                );
            }
            catch { /* never throw */ }
        }

        /// <summary>Warning dialog with custom title. Logs at Warn if configured.</summary>
        public static void WarnTitled(
            string title,
            string message,
            string stage = "unspecified",
            [CallerMemberName] string member = "",
            [CallerFilePath] string file = "",
            [CallerLineNumber] int line = 0)
        {
            WpfMessageBox.Show(message, string.IsNullOrWhiteSpace(title) ? "Warning" : title, WpfButtons.OK, WpfImage.Warning);

            if (_logs is null) return;
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
                        appVersion = _appVersion
                    }
                };

                _ = _logs.LogAsync(
                    level: LogLevel.Warn,
                    source: location,
                    eventCode: "WARN",
                    payloadObject: payload,
                    isCrash: false,
                    sessionId: _sessionIdProvider?.Invoke(),
                    stackHash: null
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

        /// <summary>Register global handlers so unhandled exceptions go through here (idempotent).</summary>
        public static void RegisterGlobalHandlers(System.Windows.Application app)
        {
            if (_handlersRegistered) return;
            _handlersRegistered = true;

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
                // Domain unhandled — still show a dialog, then allow normal termination.
                Abend(ex, "Unhandled domain exception", stage: "appdomain",
                      severity: ErrorSeverity.Critical, buttons: WpfButtons.OK, icon: WpfImage.Error, log: true);
            };

            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                try
                {
                    var flat = e.Exception?.Flatten();
                    if (flat != null)
                    {
                        foreach (var inner in flat.InnerExceptions)
                            LogBackgroundTaskFault(inner);
                    }
                    else if (e.Exception != null)
                    {
                        LogBackgroundTaskFault(e.Exception);
                    }

                    NotifyBackgroundFaultOnce();
                }
                finally
                {
                    // Prevent finalizer thread from rethrowing
                    e.SetObserved();
                }
            };
        }

        // ===== Background helpers =====

        private static void LogBackgroundTaskFault(Exception ex)
        {
            if (_logs is null) return;

            try
            {
                string? sessionId = _sessionIdProvider?.Invoke();
                var payload = new
                {
                    message = "Background task faulted",
                    exception = new
                    {
                        type = ex.GetType().FullName,
                        message = Redact(ex.Message),
                        stackTrace = ex.StackTrace
                    },
                    environment = new
                    {
                        utc = DateTime.UtcNow,
                        machine = Environment.MachineName,
                        processId = Environment.ProcessId,
                        os = Environment.OSVersion?.ToString(),
                        appVersion = _appVersion
                    }
                };

                _ = _logs.LogAsync(
                    level: LogLevel.Error,
                    source: "taskscheduler",
                    eventCode: "TASK_FAULT",
                    payloadObject: payload,
                    isCrash: false,
                    sessionId: sessionId,
                    stackHash: ShortHash(ex.ToString() ?? ex.Message ?? "taskfault")
                );
            }
            catch
            {
                // never throw from logging
            }
        }

        private static void NotifyBackgroundFaultOnce()
        {
            try
            {
                bool shouldShow;
                lock (_bgLock)
                {
                    var now = DateTime.UtcNow;
                    shouldShow = !_bgPopupVisible && (now - _bgLastShownUtc) >= _bgMinInterval;
                    if (shouldShow)
                    {
                        _bgPopupVisible = true;
                        _bgLastShownUtc = now;
                    }
                }

                if (!shouldShow) return;

                var app = System.Windows.Application.Current;
                void show()
                {
                    try
                    {
                        WpfMessageBox.Show(
                            "A background task faulted (details in logs).",
                            "Background Task Error",
                            WpfButtons.OK,
                            WpfImage.Warning);
                    }
                    finally
                    {
                        lock (_bgLock) { _bgPopupVisible = false; }
                    }
                }

                if (app?.Dispatcher != null)
                    app.Dispatcher.BeginInvoke(new Action(show));
                else
                    show();
            }
            catch
            {
                lock (_bgLock) { _bgPopupVisible = false; }
            }
        }

        // ===== Shared helpers =====

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

        private static LogLevel MapLevel(ErrorSeverity s) =>
            s switch
            {
                ErrorSeverity.Info => LogLevel.Info,
                ErrorSeverity.Warning => LogLevel.Warn,
                ErrorSeverity.Error => LogLevel.Error,
                ErrorSeverity.Critical => LogLevel.Critical,
                _ => LogLevel.Error
            };

        private static string ShortHash(string s)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(s))).Substring(0, 8);
        }

        // Very light PII/secret redaction (best-effort)
        private static string? Redact(string? s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return Regex.Replace(
                s,
                @"(?i)(password|pass|pw|key|token|secret)\s*([:=]\s*|\s+)\S+",
                m => m.Groups[1].Value + ": [REDACTED]"
            );
        }
    }
}
