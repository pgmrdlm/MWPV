// App.xaml.cs
// Purpose:
//   - Strict single-instance (Mutex + EventWaitHandle) with bring-to-front.
//   - SQLCipher & 7-Zip init, early crash handling.
//   - Run SetupPasswordAndKeyFile first, then wire encrypted logging.
//   - All popups via ErrorHandler for consistency.
// Notes:
//   - Do NOT assign EarlyLoginFailures.StoreDir (read-only). Ensure it exists with Directory.CreateDirectory(..).
//   - Use EarlyLoginFailures.PendingCount (property).
//   - FlushToDb signature: (writeDbLog: ..., deleteFile: ...).
//   - SevenZip init is best-effort via Utilities.Helpers.SevenZipHelper (non-fatal).

using MWPV.Services;                        // LogRepository (has its own LogLevel type)
using SQLitePCL;                            // SQLCipher init
using System;                               // Guid
using System.IO;                            // Path, Directory
using System.Linq;                          // FirstOrDefault over Current.Windows
using System.Reflection;                    // App version
using System.Security.Principal;            // User SID for mutex
using System.Threading;                     // Mutex, EventWaitHandle, Thread
using System.Windows;                       // Window, MessageBoxImage
using Utilities.Helpers;                    // DatabaseHelper, ErrorHandler, SevenZipHelper
using Utilities.Diagnostics;                // EarlyLoginFailures
using Utilities.Security;                   // Batteries_V2, SecureLogService, SensitiveDataCleaner
#if DEBUG
using System.Diagnostics;                   // Debug
#endif

// --- Disambiguation aliases ---
using WpfApp = System.Windows.Application;
using SecLogLevel = Utilities.Security.LogLevel;

namespace MWPV
{
    public partial class App : WpfApp
    {
        // ---- single-instance plumbing ----
        private static Mutex? _singleInstanceMutex;
        private static bool _ownsSingleInstanceMutex;              // track ownership to avoid ReleaseMutex crash
        private static EventWaitHandle? _instanceSignal;           // auto-reset event: wake first instance to focus
        private static Thread? _signalListenerThread;
        private static volatile bool _shutdownListener;

        protected override void OnStartup(StartupEventArgs e)
        {
            // --- Strict single instance per *user* (Local\ + SID) ---
            string userSid = WindowsIdentity.GetCurrent()?.User?.Value ?? "UnknownUser";
            string mutexName = $@"Local\MWPV_{userSid}";
            string eventName = $@"Local\MWPV_Signal_{userSid}";

            _singleInstanceMutex = new Mutex(initiallyOwned: true, name: mutexName, createdNew: out bool createdNew);
            _ownsSingleInstanceMutex = createdNew;

            if (!_ownsSingleInstanceMutex)
            {
                // Another instance exists -> signal it to bring its window to front, then exit.
                try
                {
                    using var existing = EventWaitHandle.OpenExisting(eventName);
                    existing.Set();
                }
                catch
                {
                    ErrorHandler.InfoTitled("Already running", "MWPV is already running.", stage: "single-instance");
                }
                WpfApp.Current?.Shutdown();
                return;
            }

            // We are the primary instance: create & start the signal listener.
            _instanceSignal = new EventWaitHandle(false, EventResetMode.AutoReset, eventName);
            _signalListenerThread = new Thread(InstanceSignalListener)
            {
                IsBackground = true,
                Name = "MWPV.InstanceSignalListener"
            };
            _signalListenerThread.Start();

            // --- Runtime init (SQLCipher, SevenZip, global error handlers) ---
            Batteries_V2.Init();                   // SQLCipher
            SevenZipHelper.ConfigureLibraryPath(); // best-effort, non-fatal
            ErrorHandler.RegisterGlobalHandlers(this);

            base.OnStartup(e);

            // Ensure early store directory exists (read-only property)
            Directory.CreateDirectory(EarlyLoginFailures.StoreDir);

            // --- Run setup to create/open encrypted DB + key ---
            WpfApp.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var setupWindow = new Utilities.Security.SetupPasswordAndKeyFile();
            bool? ok = setupWindow.ShowDialog();

            if (ok == true)
            {
                // --- Configure encrypted logging ---
                try
                {
                    string appVersion =
                        (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly())
                        .GetName().Version?.ToString() ?? "unknown";

                    string sessionId = Guid.NewGuid().ToString("N"); // per-run session id

                    var logs = new LogRepository(DatabaseHelper.OpenConnection, appVersion);

                    ErrorHandler.Configure(
                        logs,
                        sessionIdProvider: () => sessionId,
                        appVersion: appVersion
                    );

                    SecureLogService.Initialize(DatabaseHelper.OpenConnection, appVersion, "MWPV");

                    // Re-register to ensure unhandled exceptions get logged too
                    ErrorHandler.RegisterGlobalHandlers(this);

                    // Optional: sweep any unpacked SQL files
                    try
                    {
                        var sqlDir = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "MWPV", "sql");
                        SensitiveDataCleaner.SecureDeleteAllFiles(sqlDir, overwritePasses: 1);
                    }
                    catch { /* best-effort */ }

                    // --- Flush any pre-DB failures now that DB + logger are live ---
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
                                    level: SecLogLevel.Info,
                                    payload: new { earlyFail = type.ToString(), detail, occurredUtc = utc },
                                    eventCode: "EARLY_LOGIN_FAILURE",
                                    source: "SetupPasswordAndKeyFile"
                                ).GetAwaiter().GetResult(),  // bool

                            deleteFile: path =>
                            {
                                try
                                {
#if DEBUG
                                    SecureLogService.WriteAsync(
                                        SecLogLevel.Info,
                                        new { path },
                                        eventCode: "EARLY_ELOG_DELETE_ATTEMPT",
                                        source: "EarlyLoginIngest"
                                    ).GetAwaiter().GetResult();
#endif
                                    SensitiveDataCleaner.SecureFileDelete(path, overwritePasses: 1);
#if DEBUG
                                    SecureLogService.WriteAsync(
                                        SecLogLevel.Info,
                                        new { path },
                                        eventCode: "EARLY_ELOG_DELETED",
                                        source: "EarlyLoginIngest"
                                    ).GetAwaiter().GetResult();
#endif
                                    return true;
                                }
                                catch (Exception ex)
                                {
#if DEBUG
                                    SecureLogService.WriteAsync(
                                        SecLogLevel.Warn,
                                        new { path, ex = ex.Message },
                                        eventCode: "EARLY_ELOG_DELETE_FAILED",
                                        source: "EarlyLoginIngest"
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
                    // Do not block startup on logging issues
                    ErrorHandler.Abend(ex,
                        "Failed to initialize encrypted logging",
                        stage: "startup-logging",
                        severity: ErrorSeverity.Warning,
                        icon: MessageBoxImage.Warning,
                        log: false);
                }

                // --- Launch main window ---
                var mainWindow = new MainWindow();
                WpfApp.Current.MainWindow = mainWindow;
                WpfApp.Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
                mainWindow.Show();
            }
            else
            {
                WpfApp.Current.Shutdown();
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

                    // Marshal to UI thread to bring the existing instance to front
                    try { Dispatcher?.BeginInvoke(new Action(BringExistingInstanceToFront)); }
                    catch { /* never throw on listener thread */ }
                }
            }
            catch
            {
                // swallow listener errors; single-instance must not crash the app
            }
        }

        private void BringExistingInstanceToFront()
        {
            // Prefer the setup window if it's open; otherwise MainWindow or any visible window.
            Window? w =
                WpfApp.Current.Windows.OfType<Utilities.Security.SetupPasswordAndKeyFile>()
                    .FirstOrDefault(win => win.IsVisible)
                ?? WpfApp.Current.MainWindow
                ?? WpfApp.Current.Windows.Cast<Window>().FirstOrDefault(win => win.IsVisible);

            if (w == null) return;

            try
            {
                if (w.WindowState == WindowState.Minimized)
                    w.WindowState = WindowState.Normal;

                bool originalTopmost = w.Topmost;
                w.Topmost = true;
                w.Activate();
                w.Topmost = originalTopmost;
                w.Focus();
            }
            catch
            {
                // ignore activation failures
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _shutdownListener = true;

            try { _instanceSignal?.Set(); } catch { /* wake listener */ }
            try { _instanceSignal?.Dispose(); } catch { }
            _instanceSignal = null;

            try
            {
                // Only release if THIS process acquired the mutex at startup
                if (_ownsSingleInstanceMutex && _singleInstanceMutex is not null)
                {
                    _singleInstanceMutex.ReleaseMutex();
                }
            }
            catch
            {
                // If we somehow don't own it here, ignore (defensive)
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
