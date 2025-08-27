// App.xaml.cs
// Single-instance (per user SID) + bring-to-front,
// early setup dialog, then main window; encrypted logging; safe cleanup.

using MWPV.Services;                        // LogRepository
using SQLitePCL;                            // Batteries_V2
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;               // TaskScheduler.UnobservedTaskException
using System.Windows;
using System.Windows.Threading;             // DispatcherUnhandledException
using Microsoft.Win32;                      // SystemEvents.SessionEnding

using Utilities.Diagnostics;                // EarlyLoginFailures, EarlyLogIngestor
using Utilities.Helpers;                    // DatabaseHelper, ErrorHandler, SevenZipHelper
using Security.Utility;                     // SecureLogService, SensitiveDataCleaner, SecureEncryptedDataStore
using Utilities.Logging;                    // LogEventIds, LogSeverity
using Utilities.Security;              // <-- AppEntryWindow lives here now

#if DEBUG
using System.Diagnostics;
using System.Data.Common;                   // for DbCommand (debug helpers)
#endif

// Disambiguation aliases
using WpfApp = System.Windows.Application;
using SecLogLevel = Utilities.Logging.LogSeverity;
// avoid ambiguity with MWPV.Services.EarlyLogIngestor
using EarlyLogIngestor = Utilities.Diagnostics.EarlyLogIngestor;
// handy alias for the setup window
using EntryWin = Utilities.Security.AppEntryWindow;

namespace MWPV
{
    public partial class App : WpfApp
    {
        // ---- single-instance plumbing ----
        private static Mutex? _singleInstanceMutex;
        private static bool _ownsSingleInstanceMutex;
        private static EventWaitHandle? _instanceSignal;
        private static Thread? _signalListenerThread;
        private static volatile bool _shutdownListener;

        // Keep purge retention at 90 days
        private const int LogRetentionDays = 90;

        // ---- secure wipe guard ----
        private static int _wipeOnceFlag = 0;
        private static void SafeWipeAll()
        {
            try
            {
                if (Interlocked.Exchange(ref _wipeOnceFlag, 1) == 1) return;
                // never log here to avoid recursion during failure/teardown
                SensitiveDataCleaner.WipeAll();
            }
            catch
            {
                // swallow — process is exiting or faulting
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            // --- enforce single instance per user ---
            string userSid = WindowsIdentity.GetCurrent()?.User?.Value ?? "UnknownUser";
            string mutexName = $@"Local\MWPV_{userSid}";
            string eventName = $@"Local\MWPV_Signal_{userSid}";

            _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);
            _ownsSingleInstanceMutex = createdNew;

            if (!_ownsSingleInstanceMutex)
            {
                // Signal existing instance to come to front and exit this one.
                try
                {
                    using var existing = EventWaitHandle.OpenExisting(eventName);
                    existing.Set();
                }
                catch
                {
                    ErrorHandler.InfoTitled("Already running", "MWPV is already running.", "single-instance");
                }
                Current?.Shutdown();
                return;
            }

            // Listen for signals from secondary launches.
            _instanceSignal = new EventWaitHandle(false, EventResetMode.AutoReset, eventName);
            _signalListenerThread = new Thread(InstanceSignalListener)
            {
                IsBackground = true,
                Name = "MWPV.InstanceSignalListener"
            };
            _signalListenerThread.Start();

            // --- abnormal exit & OS session handlers (register EARLY) ---
            AppDomain.CurrentDomain.UnhandledException += (_, __) => SafeWipeAll();
            TaskScheduler.UnobservedTaskException += (_, e2) => { try { SafeWipeAll(); } finally { e2.SetObserved(); } };
            Current.DispatcherUnhandledException += (_, __) =>
            {
                try { SafeWipeAll(); } finally { /* ErrorHandler handles UI/log */ }
            };
            AppDomain.CurrentDomain.ProcessExit += (_, __) => SafeWipeAll();
            SystemEvents.SessionEnding += (_, __) => SafeWipeAll();

            // --- platform init & global handlers ---
            Batteries_V2.Init();                   // SQLCipher
            SevenZipHelper.ConfigureLibraryPath(); // 7-Zip helper (best effort)
            ErrorHandler.RegisterGlobalHandlers(this);

            base.OnStartup(e);

            // Ensure early-failure directory exists (for pre-login .elog files)
            Directory.CreateDirectory(EarlyLoginFailures.StoreDir);

            // --- run password/key setup before showing MainWindow ---
            Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // **Moved to app namespace**
            var setupWindow = new EntryWin();
            bool? ok = setupWindow.ShowDialog();
            if (ok != true)
            {
                Current.Shutdown();
                return;
            }

            // --- configure encrypted logging (non-fatal if it fails) ---
            string sessionId = Guid.NewGuid().ToString("N");
            string appVersion =
                (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly())
                .GetName().Version?.ToString() ?? "unknown";

            try
            {
                var logs = new LogRepository(DatabaseHelper.OpenConnection, appVersion);

                ErrorHandler.Configure(
                    logs,
                    sessionIdProvider: () => sessionId,
                    appVersion: appVersion
                );

                // Initialize secure logger before any WriteAsync calls
                SecureLogService.Initialize(DatabaseHelper.OpenConnection, appVersion, "MWPV");

                // re-hook after logger live (so global handlers can write securely)
                ErrorHandler.RegisterGlobalHandlers(this);

#if DEBUG
                Debug.WriteLine($"[LOG] Initialized encrypted logging v{appVersion}, session {sessionId}");
#endif

                // Sweep any unpacked SQL files (best effort)
                try
                {
                    var sqlDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "MWPV", "sql");
                    SensitiveDataCleaner.SecureDeleteAllFiles(sqlDir, overwritePasses: 1);
                }
                catch { /* ignore */ }

                // Standardized startup log
                LogInfo("App", "Startup (post-setup)", LogEventIds.AppStart, new { version = appVersion, sessionId });

                // Prove DB connectivity explicitly and log result
                TryProbeDb();

                // Optional: purge old logs at startup if script is present
                TryRunStartupLogPurge(LogRetentionDays);

#if DEBUG
                // Dump a few recent logs to Output window; safe & optional
                DebugDumpRecentLogs(take: 20, crashesOnly: false);
#endif

                // --- user-facing notice + ingest any early .elog files now that encrypted logging is live ---
                try
                {
                    if (EarlyLoginFailures.HasPending())
                    {
                        ErrorHandler.InfoTitled(
                            "Login Notice",
                            "Previous login failures were detected on this device.\n\n" +
                            "Details will now be ingested into the secure log.\n" +
                            "This event has been logged.",
                            "EarlyLogin.Ingest"
                        );
                    }

                    var res = EarlyLogIngestor.IngestAllEarlyLogsTransactional(DatabaseHelper.OpenConnection);

                    if (res.Inserted + res.Deduped + res.Quarantined + res.Deleted + res.Errors > 0)
                    {
                        SecureLogService.WriteAsync(
                            SecLogLevel.Info,
                            new { res.Inserted, res.Deduped, res.Quarantined, res.Deleted, res.Errors, sessionId },
                            eventCode: "EARLY_INGEST_SUMMARY",
                            source: "App.Startup"
                        ).GetAwaiter().GetResult();
                    }
                }
                catch (Exception exIngest)
                {
                    SecureLogService.WriteAsync(
                        SecLogLevel.Warn,
                        new { ex = exIngest.Message },
                        eventCode: "EARLY_INGEST_FAILED",
                        source: "App.Startup"
                    ).GetAwaiter().GetResult();
                }
            }
            catch (Exception ex)
            {
                // Don’t block startup if logging init fails
                ErrorHandler.Abend(
                    ex,
                    "Failed to initialize encrypted logging",
                    stage: "startup-logging",
                    severity: ErrorSeverity.Warning,
                    icon: MessageBoxImage.Warning,
                    log: false
                );
            }

            // --- show MainWindow (now single source of creation; App.xaml has no StartupUri) ---
            var main = new MainWindow();
            Current.MainWindow = main;
            Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
            main.Show();
        }

        // Try to purge old log rows using Logs_Purge_OlderThan.sql (if present in the key archive)
        private static void TryRunStartupLogPurge(int retentionDays)
        {
            try
            {
                string sql = SecureEncryptedDataStore.GetString("Logs_Purge_OlderThan.sql");
                if (string.IsNullOrWhiteSpace(sql))
                    return; // script not present yet → skip silently

                using var conn = DatabaseHelper.GetAppOpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;

                var cutoff = DateTime.UtcNow.AddDays(-Math.Abs(retentionDays));
                var p = cmd.CreateParameter();
                p.ParameterName = "@CutoffUtc";
                p.DbType = System.Data.DbType.DateTime;
                p.Value = cutoff;
                cmd.Parameters.Add(p);

                int n = cmd.ExecuteNonQuery();

#if DEBUG
                // Visible in VS Output window for quick verification
                Debug.WriteLine($"[LOG Purge] purged={n} cutoffUtc={cutoff:O} retentionDays={retentionDays}");
#endif
                // Persisted in DB for audit
                SecureLogService.WriteAsync(
                    SecLogLevel.Info,
                    new { purged = n, cutoffUtc = cutoff, retentionDays },
                    eventCode: "LOGS_PURGE_STARTUP",
                    source: "App.Startup"
                ).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine($"[LOG Purge] FAILED: {ex.Message}");
#endif
                SecureLogService.WriteAsync(
                    SecLogLevel.Warn,
                    new { ex = ex.Message },
                    eventCode: "LOGS_PURGE_STARTUP_FAILED",
                    source: "App.Startup"
                ).GetAwaiter().GetResult();
            }
        }

#if DEBUG
        // ---- debug-only helpers ----

        private static void DebugDumpRecentLogs(int take = 20, bool crashesOnly = false)
        {
            try
            {
                string sql = SecureEncryptedDataStore.GetString("Logs_Select_Recent.sql");
                if (string.IsNullOrWhiteSpace(sql)) return;

                using var conn = DatabaseHelper.GetAppOpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;

                // Add only if referenced by the script (future-proof).
                AddOptionalParameter(cmd, "@Take", take);
                AddOptionalParameter(cmd, "@Limit", take);
                AddOptionalParameter(cmd, "@CrashesOnly", crashesOnly ? 1 : 0);

                // Ensure @FromUtc exists (NULL is allowed; SQL checks IS NULL)
                AddOptionalParameter(cmd, "@FromUtc", DBNull.Value);

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var id = r["Id"];
                    var created = r["CreatedUtc"];
                    var level = r["Level"];
                    var evt = r["EventCode"];
                    var source = r["Source"];
                    var payload = r["Payload"];
                    Debug.WriteLine($"[LOG RECENT] #{id} {created} L={level} {source} {evt} :: {payload}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LOG RECENT] failed: {ex.Message}");
            }
        }

        /// <summary>Adds a parameter only if the SQL text references it.</summary>
        private static void AddOptionalParameter(DbCommand cmd, string name, object value)
        {
            if (!cmd.CommandText.Contains(name, StringComparison.OrdinalIgnoreCase)) return;
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }
#endif

        // --- small centralized wrappers to carry your numeric EventIds via SecureLogService ---
        private static void LogInfo(string source, string message, int eventId, object? details = null)
            => Log(SecLogLevel.Info, source, message, eventId, details);

        private static void LogWarn(string source, string message, int eventId, object? details = null)
            => Log(SecLogLevel.Warn, source, message, eventId, details);

        private static void LogError(string source, string message, int eventId, object? details = null)
            => Log(SecLogLevel.Error, source, message, eventId, details);

        private static void Log(SecLogLevel level, string source, string message, int eventId, object? details)
        {
            try
            {
                SecureLogService.WriteAsync(
                    level,
                    new { eventId, message, details },
                    eventCode: $"EVENT_{eventId}",
                    source: source ?? "App"
                ).GetAwaiter().GetResult();

#if DEBUG
                Debug.WriteLine($"[LOG {level}] {source} #{eventId} {message}");
#endif
            }
            catch
            {
                // Intentionally swallow to avoid recursive logging/abends.
            }
        }

        private static void TryProbeDb()
        {
            try
            {
                using var _ = DatabaseHelper.GetAppOpenConnection();
                LogInfo("DB", "Connection opened", LogEventIds.DbOpenSucceeded);
            }
            catch (Exception ex)
            {
                LogError("DB", "Connection failed", LogEventIds.DbOpenFailed,
                    new { exType = ex.GetType().Name, exMessage = ex.Message });
            }
        }

        private void InstanceSignalListener()
        {
            try
            {
                while (!_shutdownListener && _instanceSignal is not null)
                {
                    _instanceSignal.WaitOne();
                    if (_shutdownListener) break;

                    try { Dispatcher?.BeginInvoke(new Action(BringExistingInstanceToFront)); }
                    catch { /* never throw on listener thread */ }
                }
            }
            catch
            {
                // Do not crash on listener errors
            }
        }

        private void BringExistingInstanceToFront()
        {
            // Prefer setup window if visible; otherwise MainWindow; otherwise any visible window.
            Window? w =
                Current.Windows.OfType<EntryWin>()
                    .FirstOrDefault(win => win.IsVisible)
                ?? Current.MainWindow
                ?? Current.Windows.Cast<Window>().FirstOrDefault(win => win.IsVisible);

            if (w == null) return;

            try
            {
                if (w.WindowState == WindowState.Minimized)
                    w.WindowState = WindowState.Normal;

                bool wasTop = w.Topmost;
                w.Topmost = true;
                w.Activate();
                w.Topmost = wasTop;
                w.Focus();
            }
            catch
            {
                // ignore activation issues
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Wipe first on normal exit
            SafeWipeAll();

            _shutdownListener = true;

            try { _instanceSignal?.Set(); } catch { }
            try { _instanceSignal?.Dispose(); } catch { }
            _instanceSignal = null;

            try
            {
                if (_ownsSingleInstanceMutex && _singleInstanceMutex is not null)
                    _singleInstanceMutex.ReleaseMutex();
            }
            catch
            {
                // ignore if we somehow don't own it
            }
            finally
            {
                try { _singleInstanceMutex?.Dispose(); } catch { }
                _singleInstanceMutex = null;
                _ownsSingleInstanceMutex = false;
            }

            base.OnExit(e);
        }
    }
}
