using MWPV.Services;                        // LogRepository
using SQLitePCL;                             // SQLCipher init
using System.Reflection;                    // App version
using System.Security.Principal;            // User SID for mutex
using System.Threading;                     // Mutex
using System.Windows;
using Utilities.Helpers;                    // DatabaseHelper, ErrorHandler
using Utilities.Security;                   // Batteries_V2, ErrorHandler
using MessageBox = System.Windows.MessageBox;   // Alias

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
                MessageBox.Show("MWPV is already running.", "Already running",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                Current.Shutdown();
                return;
            }

            // --- SQLCipher runtime init ---
            Batteries_V2.Init();

            // --- Register global error handlers early (UI won't crash even before DB setup) ---
            ErrorHandler.RegisterGlobalHandlers(this);

            base.OnStartup(e);

            // Run setup to create/open encrypted DB + key (this must set up the password store)
            Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var setupWindow = new SetupPasswordAndKeyFile();
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
                    string sessionId = System.Guid.NewGuid().ToString("N");

                    // Prefer factory so no raw connection string is kept around
                    var logs = new LogRepository(DatabaseHelper.GetAppOpenConnection, appVersion);

                    ErrorHandler.Configure(
                        logs,
                        sessionIdProvider: () => sessionId,
                        appVersion: appVersion
                    );

                    // Re-register so unhandled exceptions from here on also get logged
                    ErrorHandler.RegisterGlobalHandlers(this);

                    // Smoke test so you can confirm logging without forcing an error
                    ErrorHandler.Info($"Logging online (v{appVersion})", stage: "startup");
                }
                catch (System.Exception ex)
                {
                    // Don’t block startup if logging wiring fails
                    ErrorHandler.Abend(ex,
                        "Failed to initialize encrypted logging",
                        stage: "startup-logging",
                        severity: ErrorSeverity.Warning,
                        icon: MessageBoxImage.Warning,
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
