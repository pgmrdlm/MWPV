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
using System.Windows;
using Utilities.Diagnostics;                // EarlyLoginFailures
using Utilities.Helpers;                    // DatabaseHelper, ErrorHandler, SevenZipHelper
using Utilities.Security;                   // SecureLogService, SensitiveDataCleaner
#if DEBUG
using System.Diagnostics;
#endif

// Disambiguation aliases
using WpfApp = System.Windows.Application;
using SecLogLevel = Utilities.Security.LogLevel;

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
                try { using var existing = EventWaitHandle.OpenExisting(eventName); existing.Set(); }
                catch { ErrorHandler.InfoTitled("Already running", "MWPV is already running.", "single-instance"); }
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

            // --- platform init & global handlers ---
            Batteries_V2.Init();                   // SQLCipher
            SevenZipHelper.ConfigureLibraryPath(); // 7-Zip helper (best effort)
            ErrorHandler.RegisterGlobalHandlers(this);

            base.OnStartup(e);

            // Ensure early-failure directory exists
            Directory.CreateDirectory(EarlyLoginFailures.StoreDir);

            // --- run password/key setup before showing MainWindow ---
            Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var setupWindow = new Utilities.Security.SetupPasswordAndKeyFile();
            bool? ok = setupWindow.ShowDialog();

            if (ok != true)
            {
                Current.Shutdown();
                return;
            }

            // --- configure encrypted logging (non-fatal if it fails) ---
            try
            {
                string appVersion =
                    (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly())
                    .GetName().Version?.ToString() ?? "unknown";

                string sessionId = Guid.NewGuid().ToString("N");

                var logs = new LogRepository(DatabaseHelper.OpenConnection, appVersion);

                ErrorHandler.Configure(
                    logs,
                    sessionIdProvider: () => sessionId,
                    appVersion: appVersion
                );

                SecureLogService.Initialize(DatabaseHelper.OpenConnection, appVersion, "MWPV");
                ErrorHandler.RegisterGlobalHandlers(this); // re-hook after logger live

                // Sweep any unpacked SQL files (best effort)
                try
                {
                    var sqlDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "MWPV", "sql");
                    SensitiveDataCleaner.SecureDeleteAllFiles(sqlDir, overwritePasses: 1);
                }
                catch { /* ignore */ }

                // Ingest any early failures now that DB is ready
                if (EarlyLoginFailures.HasPending())
                {
                    SecureLogService.WriteAsync(
                        SecLogLevel.Info,
                        new { pending = EarlyLoginFailures.PendingCount, dir = EarlyLoginFailures.StoreDir },
                        eventCode: "EARLY_LOGIN_FAILURES_PENDING",
                        source: "post-login"
                    ).GetAwaiter().GetResult();

                    EarlyLoginFailures.FlushToDb(
                        writeDbLog: (utc, type, detail) =>
                            SecureLogService.WriteAsync(
                                SecLogLevel.Info,
                                new { earlyFail = type.ToString(), detail, occurredUtc = utc },
                                eventCode: "EARLY_LOGIN_FAILURE",
                                source: "SetupPasswordAndKeyFile"
                            ).GetAwaiter().GetResult(),
                        deleteFile: path =>
                        {
                            try
                            {
#if DEBUG
                                SecureLogService.WriteAsync(
                                    SecLogLevel.Info, new { path },
                                    eventCode: "EARLY_ELOG_DELETE_ATTEMPT", source: "EarlyLoginIngest"
                                ).GetAwaiter().GetResult();
#endif
                                SensitiveDataCleaner.SecureFileDelete(path, overwritePasses: 1);
#if DEBUG
                                SecureLogService.WriteAsync(
                                    SecLogLevel.Info, new { path },
                                    eventCode: "EARLY_ELOG_DELETED", source: "EarlyLoginIngest"
                                ).GetAwaiter().GetResult();
#endif
                                return true;
                            }
                            catch (Exception ex)
                            {
#if DEBUG
                                SecureLogService.WriteAsync(
                                    SecLogLevel.Warn, new { path, ex = ex.Message },
                                    eventCode: "EARLY_ELOG_DELETE_FAILED", source: "EarlyLoginIngest"
                                ).GetAwaiter().GetResult();
#endif
                                return false;
                            }
                        }
                    );
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
                Current.Windows.OfType<Utilities.Security.SetupPasswordAndKeyFile>()
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
