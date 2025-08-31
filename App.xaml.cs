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
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;                      // SystemEvents.SessionEnding

using Utilities.Diagnostics;                // EarlyLoginFailures, EarlyLogIngestor
using Utilities.Helpers;                    // DatabaseHelper, ErrorHandler, SevenZipHelper
using Security.Utility;                     // SecureLogService, SensitiveDataCleaner, SecureEncryptedDataStore
using Utilities.Security;                   // AppEntryWindow

#if DEBUG
using System.Diagnostics;
using System.Data.Common;                   // DbCommand (debug helpers)
#endif

using WpfApp = System.Windows.Application;
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

        private const int LogRetentionDays = 90;

        // ---- secure wipe + lifecycle guards ----
        private static int _wipeOnceFlag = 0;
        private static volatile bool _shuttingDown;

        // ---- background fault buffering (pre-logging) ----
        private static volatile bool _loggingReady;
        private static readonly ConcurrentQueue<string> _preInitBgFaults = new();
        private static int _preInitBgFaultsDropped;
        private static int _bgFaultToastShown;

        private static void SafeWipeAll()
        {
            try
            {
                if (Interlocked.Exchange(ref _wipeOnceFlag, 1) == 1) return;
                SensitiveDataCleaner.WipeAll();
            }
            catch { /* process is exiting or faulting */ }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            // Observe unobserved task faults as early as possible (buffer until logging is ready)
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            // --- enforce single instance per user ---
            string userSid = WindowsIdentity.GetCurrent()?.User?.Value ?? "UnknownUser";
            string mutexName = $@"Local\MWPV_{userSid}";
            string eventName = $@"Local\MWPV_Signal_{userSid}";

            _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);
            _ownsSingleInstanceMutex = createdNew;

            if (!_ownsSingleInstanceMutex)
            {
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

            // --- OS/session teardown hooks (do NOT wipe in exception handlers) ---
            AppDomain.CurrentDomain.ProcessExit += (_, __) => SafeWipeAll();
            SystemEvents.SessionEnding += (_, __) => SafeWipeAll();

            // --- platform init ---
            Batteries_V2.Init();                   // SQLCipher
            SevenZipHelper.ConfigureLibraryPath(); // best effort

            // NOTE: Do NOT call ErrorHandler.RegisterGlobalHandlers yet—only after secure logging is live.

            base.OnStartup(e);

            // Ensure early-failure directory exists (for pre-login .elog files)
            Directory.CreateDirectory(EarlyLoginFailures.StoreDir);

            // --- run password/key setup before showing MainWindow ---
            Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;

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

                // Now that the logger is live, hook ErrorHandler global handlers ONCE.
                ErrorHandler.RegisterGlobalHandlers(this);

                // Optional lightweight wrappers for extra telemetry; keep after RegisterGlobalHandlers
                AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
                Current.DispatcherUnhandledException += Current_DispatcherUnhandledException;

                _loggingReady = true;
                FlushPreInitFaults();

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

                // DB smoke + maintenance
                TryProbeDb();
                TryRunStartupLogPurge(LogRetentionDays);

#if DEBUG
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

                    var res = EarlyLogIngestor.IngestAllEarlyLogsTransactionalToLogs(
                        earlyDir: EarlyLoginFailures.StoreDir,
                        sessionId: sessionId,
                        appVersion: appVersion,
                        openConnection: DatabaseHelper.GetAppOpenConnection,
                        pattern: "*.elogp",
                        deleteOnSuccess: true,
                        source: "EarlyIngest",
                        level: "WARN"
                    );

                    SecureLogService.WriteAsync(
                        SecLogLevel.Info,
                        new { res.Found, res.Inserted, res.Deduped, res.Quarantined, res.Deleted, res.Errors, sessionId },
                        eventCode: "EARLY_INGEST_SUMMARY",
                        source: "App.Startup"
                    ).GetAwaiter().GetResult();
                }
                catch (Exception exIngest)
                {
                    // Swallow/record but do NOT wipe here.
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
                Debug.WriteLine($"[LOG Purge] purged={n} cutoffUtc={cutoff:O} retentionDays={retentionDays}");
#endif
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
                if (_shuttingDown) return;
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
            catch { /* ignore to avoid recursion */ }
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
            catch { /* ignore */ }
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
            catch { /* ignore activation issues */ }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _shuttingDown = true;

            // Unhook handlers so nothing logs after we start tearing down
            try { TaskScheduler.UnobservedTaskException -= TaskScheduler_UnobservedTaskException; } catch { }
            try { Current.DispatcherUnhandledException -= Current_DispatcherUnhandledException; } catch { }
            try { AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException; } catch { }

            // Stop the bring-to-front listener cleanly
            _shutdownListener = true;
            try { _instanceSignal?.Set(); } catch { }
            try { _signalListenerThread?.Join(250); } catch { }

            // Wipe after we stop background noise
            SafeWipeAll();

            try { _instanceSignal?.Dispose(); } catch { }
            _instanceSignal = null;

            try
            {
                if (_ownsSingleInstanceMutex && _singleInstanceMutex is not null)
                    _singleInstanceMutex.ReleaseMutex();
            }
            catch { }
            finally
            {
                try { _singleInstanceMutex?.Dispose(); } catch { }
                _singleInstanceMutex = null;
                _ownsSingleInstanceMutex = false;
            }

            base.OnExit(e);
        }

        // Prove DB connectivity explicitly and log result
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

        // ---- background fault plumbing ----
        private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            try
            {
                var exText = e.Exception?.ToString() ?? "(null)";

                if (!_loggingReady)
                {
                    if (_preInitBgFaults.Count < 5) _preInitBgFaults.Enqueue(exText);
                    else Interlocked.Increment(ref _preInitBgFaultsDropped);
                }
                else
                {
                    try
                    {
                        if (!_shuttingDown)
                        {
                            SecureLogService.WriteAsync(
                                SecLogLevel.Error,
                                new { ex = exText },
                                eventCode: "BG_TASK_FAULT",
                                source: "App"
                            ).GetAwaiter().GetResult();
                        }
                    }
                    catch { /* ignore store/teardown issues */ }

                    if (Interlocked.Exchange(ref _bgFaultToastShown, 1) == 0)
                    {
                        try
                        {
                            ErrorHandler.InfoTitled(
                                "Background Task Error",
                                "A background task faulted (details in logs).",
                                "BG_TASK_FAULT");
                        }
                        catch { }
                    }
                }
            }
            finally
            {
                e.SetObserved(); // prevent GC rethrow
            }
        }

        private static void FlushPreInitFaults()
        {
            if (_preInitBgFaults.IsEmpty && _preInitBgFaultsDropped == 0) return;

            try
            {
                var faults = _preInitBgFaults.ToArray();
                if (!_shuttingDown)
                {
                    SecureLogService.WriteAsync(
                        SecLogLevel.Error,
                        new { count = faults.Length, dropped = _preInitBgFaultsDropped, faults },
                        eventCode: "BG_TASK_FAULT_PREINIT",
                        source: "App"
                    ).GetAwaiter().GetResult();
                }
            }
            catch { /* ignore */ }

            if (Interlocked.Exchange(ref _bgFaultToastShown, 1) == 0)
            {
                try
                {
                    ErrorHandler.InfoTitled(
                        "Background Task Error",
                        "One or more background tasks faulted during startup (details in logs).",
                        "BG_TASK_FAULT_PREINIT");
                }
                catch { }
            }

            while (_preInitBgFaults.TryDequeue(out _)) { }
            _preInitBgFaultsDropped = 0;
        }

        // Extra telemetry wrappers (non-fatal; ErrorHandler shows its own UI)
        private static void CurrentDomain_UnhandledException(object? sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                if (_loggingReady && !_shuttingDown)
                {
                    SecureLogService.WriteAsync(
                        SecLogLevel.Error,
                        new { ex = e.ExceptionObject?.ToString(), isTerminating = e.IsTerminating },
                        eventCode: "UNHANDLED_DOMAIN",
                        source: "App.Wrapper"
                    ).GetAwaiter().GetResult();
                }
            }
            catch { }
        }

        private static void Current_DispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                if (_loggingReady && !_shuttingDown)
                {
                    SecureLogService.WriteAsync(
                        SecLogLevel.Error,
                        new { ex = e.Exception?.ToString() },
                        eventCode: "UNHANDLED_DISPATCHER",
                        source: "App.Wrapper"
                    ).GetAwaiter().GetResult();
                }
            }
            catch { }
            // let ErrorHandler's own handler decide about e.Handled
        }
    }
}
