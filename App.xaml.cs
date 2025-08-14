// App.xaml.cs
// Purpose:
//   - Single-instance gate, SQLCipher init, early crash handling.
//   - Run SetupPasswordAndKeyFile first, then wire encrypted logging.
//   - Replace any direct MessageBox.Show with ErrorHandler.InfoTitled for consistency.
//
// Why this change:
//   Using ErrorHandler ensures *all* user-facing popups go through one place,
//   and (once configured) those events get logged securely too. It keeps titles
//   consistent and avoids ad-hoc MessageBox calls scattered around the app.

using MWPV.Services;                        // LogRepository
using SQLitePCL;                             // SQLCipher init
using System;                                // Guid
using System.IO;                             // Path, Directory
using System.Reflection;                     // App version
using System.Security.Principal;             // User SID for mutex
using System.Threading;                      // Mutex
using System.Windows;                        // Application, Window
using Utilities.Helpers;                     // DatabaseHelper, ErrorHandler
using Utilities.Diagnostics;                 // EarlyLoginFailures
using Utilities.Security;                    // Batteries_V2, EarlyLoginFailures, SecureLogService, SensitiveDataCleaner
#if DEBUG
using System.Diagnostics;                    // Debug
#endif

namespace MWPV
{
    public partial class App : System.Windows.Application
    {
        private static Mutex _singleInstanceMutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            // --- Single instance per user/session ---
            bool createdNew;
            string userSid = WindowsIdentity.GetCurrent()?.User?.Value ?? "UnknownUser";
            string mutexName = $@"Local\MWPV_{userSid}";
            _singleInstanceMutex = new Mutex(initiallyOwned: true, name: mutexName, createdNew: out createdNew);
            if (!createdNew)
            {
                // (Changed) Use centralized helper instead of MessageBox.Show
                ErrorHandler.InfoTitled("Already running", "MWPV is already running.", stage: "single-instance");
                Current.Shutdown();
                return;
            }

            // --- SQLCipher runtime init ---
            Batteries_V2.Init();

            // --- SevenZip runtime init ---
            SevenZipHelper.ConfigureLibraryPath();

            // --- Register global error handlers early (UI won't crash even before DB setup) ---
            ErrorHandler.RegisterGlobalHandlers(this);

            base.OnStartup(e);

            // Optional: set explicit early store dir (defaults to %LOCALAPPDATA%\MWPV\early if not set)
            EarlyLoginFailures.StoreDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MWPV", "early");
            Directory.CreateDirectory(EarlyLoginFailures.StoreDir);

            // Run setup to create/open encrypted DB + key (this must set up the password store)
            Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var setupWindow = new Utilities.Security.SetupPasswordAndKeyFile();
            bool? ok = setupWindow.ShowDialog();

            if (ok == true)
            {
                // --- Configure encrypted logging (no schema creation; will fail if Logs is missing) ---
                try
                {
                    string appVersion =
                        (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly())
                        .GetName().Version?.ToString() ?? "unknown";

                    // Stable session id per app run
                    string sessionId = Guid.NewGuid().ToString("N");

                    // Prefer factory so no raw connection string is kept around
                    var logs = new LogRepository(DatabaseHelper.GetAppOpenConnection, appVersion);

                    ErrorHandler.Configure(
                        logs,
                        sessionIdProvider: () => sessionId,
                        appVersion: appVersion
                    );

                    // Initialize SecureLogService so we can log pending early failures
                    SecureLogService.Initialize(DatabaseHelper.GetAppOpenConnection, appVersion, "MWPV");

                    // Re-register so unhandled exceptions from here on also get logged
                    ErrorHandler.RegisterGlobalHandlers(this);

                    // (Optional) sweep stray unpacked SQL files on startup
                    try
                    {
                        var sqlDir = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "MWPV", "sql");
                        SensitiveDataCleaner.SecureDeleteAllFiles(sqlDir, overwritePasses: 1);
                    }
                    catch { /* best-effort */ }

                    // --- Quiet notify + flush any pre-DB failures now that DB + logger are live ---
                    if (EarlyLoginFailures.HasPending())
                    {
                        // Log-only (no UI popup)
                        SecureLogService.WriteAsync(
                            Utilities.Security.LogLevel.Info,
                            new { pending = EarlyLoginFailures.PendingCount(), dir = EarlyLoginFailures.StoreDir },
                            eventCode: "EARLY_LOGIN_FAILURES_PENDING",
                            source: "post-login"
                        ).GetAwaiter().GetResult();

                        // Set quarantine dir once
                        var earlyDir = EarlyLoginFailures.StoreDir;
                        var quarantineDir = Path.Combine(earlyDir, "quarantine");
                        Directory.CreateDirectory(quarantineDir);

                        // Write a log row per pending .elog, then securely delete each file (via quarantine)
                        EarlyLoginFailures.FlushToDb(
                            writeDbLog: (utc, type, detail) =>
                                SecureLogService.WriteAsync(
                                    level: Utilities.Security.LogLevel.Info,
                                    payload: new { earlyFail = type.ToString(), detail, occurredUtc = utc },
                                    eventCode: "EARLY_LOGIN_FAILURE",
                                    source: "SetupPasswordAndKeyFile"
                                ).GetAwaiter().GetResult(),  // wait so we only delete on success

                            secureFileDelete: path =>
                            {
#if DEBUG
                                // Breadcrumb: which file are we deleting?
                                SecureLogService.WriteAsync(
                                    Utilities.Security.LogLevel.Info,
                                    new { path },
                                    eventCode: "EARLY_ELOG_DELETE_ATTEMPT",
                                    source: "EarlyLoginIngest"
                                ).GetAwaiter().GetResult();
#endif
                                var res = SensitiveDataCleaner.QuarantineThenSecureDelete(
                                    srcPath: path,
                                    quarantineDir: quarantineDir,
                                    overwritePasses: 1,
                                    maxRetries: 5
                                );

#if DEBUG
                                SecureLogService.WriteAsync(
                                    res.Success ? Utilities.Security.LogLevel.Info : Utilities.Security.LogLevel.Warn,
                                    res.Success ? new { path } : new { path, res.Error, res.Detail },
                                    eventCode: res.Success ? "EARLY_ELOG_DELETED" : "EARLY_ELOG_DELETE_FAILED",
                                    source: "EarlyLoginIngest"
                                ).GetAwaiter().GetResult();
#endif
                                return res.Success;
                            }
                        );
                    }
                }
                catch (System.Exception ex)
                {
                    // Don’t block startup if logging wiring fails
                    ErrorHandler.Abend(ex,
                        "Failed to initialize encrypted logging",
                        stage: "startup-logging",
                        severity: ErrorSeverity.Warning,
                        icon: System.Windows.MessageBoxImage.Warning,
                        log: false);
                }

                // --- Launch main window ---
                var mainWindow = new MainWindow();
                Current.MainWindow = mainWindow;
                Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
                mainWindow.Show();
            }
            else
            {
                Current.Shutdown();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
            _singleInstanceMutex = null;
            base.OnExit(e);
        }
    }
}
