// File: App.xaml.cs — full file (WPF, .NET 8)
using MWPV.Services;                   // ✅ LogCatalogService (for SESSION_END)
using MWPV.Services.AppLifecycle;
using MWPV.Services.Security;
using Security.Utility.Wiping;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;         // ✅ for TextBlock (StatusText)
using System.Windows.Input;            // ✅ for InputManager + input event args
using Utilities.Diagnostics;           // EarlyLoginFailures, EarlyLogIngestor
using Utilities.Helpers;               // ErrorHandler
using Utilities.Security;

namespace MWPV
{
    public partial class App : Application
    {
        // Single-instance IDs (Global = cross-session)
        private const string MutexName = @"Global\MWPV.App.SingleInstance";
        private const string BringToFrontEventName = @"Global\MWPV.App.BringToFront";

        private Mutex? _singleInstanceMutex;
        private EventWaitHandle? _bringToFrontEvent;
        private Thread? _bringToFrontListener;
        private bool _ownsMutex;
        private static int _sensitiveShutdownCleanupRan;

        // ===== Inactivity input hook (app-scoped) =====
        // Any app component can subscribe and reset its inactivity timer.
        // This stays generic: App only reports "user activity happened in MWPV".
        public static event Action? UserActivityDetected;

        private bool _inputHooked;

        protected override void OnStartup(StartupEventArgs e)
        {
            AppRunState.MigrationFlag = CaptureMigrationFlag(e.Args);
            var defaultUpgradeFlagPath = GetDefaultUpgradeFlagPath();
            AppRunState.StartupContext = AppStartupDetector.Detect(
                e.Args,
                databaseExists: File.Exists(DatabaseHelper.GetAppDbPath()),
                upgradeFlagFileExists: File.Exists(defaultUpgradeFlagPath),
                defaultUpgradeFlagFilePath: defaultUpgradeFlagPath);

            // ---- Single-instance gate ----
            _singleInstanceMutex = new Mutex(initiallyOwned: true, name: MutexName, createdNew: out _ownsMutex);
            if (!_ownsMutex)
            {
                try { using var evt = EventWaitHandle.OpenExisting(BringToFrontEventName); evt.Set(); } catch { }
                try { EarlyLoginFailures.Write("App", "Second instance detected; signaling first and exiting."); } catch { }
                Shutdown();
                return;
            }

            base.OnStartup(e);

            // IMPORTANT: never allow auto-shutdown while we negotiate the entry dialog
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // ---- Bring-to-front listener ----
            try
            {
                _bringToFrontEvent = new EventWaitHandle(false, EventResetMode.AutoReset, BringToFrontEventName);
                _bringToFrontListener = new Thread(BringToFrontListenLoop)
                {
                    IsBackground = true,
                    Name = "MWPV.BringToFrontListener"
                };
                _bringToFrontListener.Start();
            }
            catch (Exception ex)
            {
                try { EarlyLoginFailures.Write("App", "Failed to start BringToFront listener", ex: ex); } catch { }
            }

            // ---- Early log folders only (no ingest before login) ----
            try
            {
                Directory.CreateDirectory(EarlyLoginFailures.StoreDir);
                Directory.CreateDirectory(EarlyLoginFailures.QuarantineDir);
                // NOTE: We intentionally DO NOT call EarlyLogIngestor.IngestAll() here.
                // Ingest runs ONLY after a successful key/database login.
            }
            catch (Exception ex)
            {
                try { EarlyLoginFailures.Write("EarlyIngestor", "Failed to ensure early log directories", ex: ex); } catch { }
            }

            // ===== Abnormal-exit hooks (best-effort) =====
            // Only log SESSION_END if a valid login occurred this run.
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                try
                {
                    if (AppRunState.DbOpenedThisRun && !AppRunState.EndLogged)
                    {
                        LogCatalogService.InsertSessionEnd("UnhandledException", isError: true, exitCode: 1);
                        AppRunState.EndLogged = true;
                    }
                }
                catch { /* swallow */ }

                try
                {
                    var fatalException = args.ExceptionObject as Exception;
                    FatalErrorPopupHelper.ShowFatalAsync(
                        "MWPV encountered a fatal error and must close.",
                        fatalException,
                        "An unhandled application exception reached the global boundary.").GetAwaiter().GetResult();
                }
                catch
                {
                    ForceFatalShutdown();
                }
            };

            this.DispatcherUnhandledException += async (_, args) =>
            {
                try
                {
                    if (AppRunState.DbOpenedThisRun && !AppRunState.EndLogged)
                    {
                        LogCatalogService.InsertSessionEnd("DispatcherUnhandledException", isError: true, exitCode: 1);
                        AppRunState.EndLogged = true;
                    }
                }
                catch { /* swallow */ }

                try
                {
                    args.Handled = true;
                    await FatalErrorPopupHelper.ShowFatalAsync(
                        "MWPV encountered a fatal error and must close.",
                        args.Exception,
                        "An unhandled UI exception reached the application dispatcher.");
                }
                catch
                {
                    ForceFatalShutdown();
                }
            };
            // =============================================

            // ---- Entry flow (modal) -> then MainWindow ----
            try
            {
                var entry = new AppEntryWindow();
                var ok = entry.ShowDialog() == true;   // modal; null/false if closed/cancelled
                if (!ok)
                {
                    AppExit.Shutdown(this, AppExitCode.UserCancelledLogin, "Entry window was cancelled or closed.");
                    return;
                }

                // Count any pending early logs (quiet status later; no modal)
                int pending = 0;
                try
                {
                    var earlyDir = EarlyLoginFailures.StoreDir;
                    if (Directory.Exists(earlyDir))
                        pending = Directory.GetFiles(earlyDir, "*.elogp", SearchOption.TopDirectoryOnly).Length;
                }
                catch { /* best-effort */ }

                // Post-login ingest to catch files created during THIS run (e.g., bad pw then good)
                try
                {
                    EarlyLogIngestor.IngestAll();
                }
                catch (Exception ex)
                {
                    try { EarlyLoginFailures.Write("EarlyIngestor", "Post-login ingest failed", ex: ex); } catch { }
                }

                if (pending > 0)
                {
                    string statusMessage = pending == 1
                        ? "1 prior invalid login attempt was ingested into the audit log."
                        : $"{pending} prior invalid login attempts were ingested into the audit log.";

                    AppStatusMessageService.Publish(
                        statusMessage,
                        AppStatusMessageKind.Info,
                        TimeSpan.FromSeconds(8));
                }

                // Create and show main window, THEN restore normal shutdown behavior
                if (!Dispatcher.HasShutdownStarted)
                {
                    var main = new MainWindow();
                    main.Title = "MWPV - My Windows Password Vault";   // <-- ONLY CHANGE
                    MainWindow = main;
                    ShutdownMode = ShutdownMode.OnMainWindowClose;  // <-- set ONLY after MainWindow exists
                    main.Show();

                    // ✅ Inactivity input hook: keystrokes + mouse clicks/wheel inside MWPV
                    HookUserInput();

                }
            }
            catch (Exception ex)
            {
                try { EarlyLoginFailures.Write("App", "Failed to show initial window(s)", ex: ex); } catch { }
                AppExit.Set(AppExitCode.UnknownFatalError, "Startup failed while showing the entry or main window.");
                _ = FatalErrorPopupHelper.ShowFatalAsync(
                    "MWPV could not finish starting and must close.",
                    ex,
                    "Startup failed while showing the entry window or main window.");
                return;
            }
            // NOTE: No 'finally' that touches ShutdownMode — avoids the crash when app is already shutting down.
        }

        private static string? CaptureMigrationFlag(string[]? args)
        {
            if (args == null || args.Length == 0)
                return null;

            const string name = "migration_flag";
            const string prefix = name + "=";

            foreach (var arg in args)
            {
                if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase))
                    return string.Empty;

                if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return arg[prefix.Length..];
            }

            return null;
        }

        private static string GetDefaultUpgradeFlagPath()
        {
            var dbDirectory = Path.GetDirectoryName(DatabaseHelper.GetAppDbPath());
            return Path.Combine(
                string.IsNullOrWhiteSpace(dbDirectory) ? AppContext.BaseDirectory : dbDirectory,
                "upgrade.pending.json");
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // >>> SESSION_END on normal exit (if login happened this run)
            try
            {
                if (AppRunState.DbOpenedThisRun && !AppRunState.EndLogged)
                {
                    LogCatalogService.InsertSessionEnd(
                        reason: "NormalExit",
                        isError: false,
                        exitCode: e?.ApplicationExitCode
                    );
                    AppRunState.EndLogged = true;
                }
            }
            catch { /* swallow */ }

            RunSensitiveShutdownCleanup();

            // ✅ Unhook inactivity input hook
            UnhookUserInput();

            // Order matters: stop thread -> signal -> dispose, then mutex
            try { _bringToFrontListener?.Interrupt(); } catch { }     // break WaitOne()
            try { _bringToFrontEvent?.Set(); } catch { }              // nudge if not interrupted

            try
            {
                if (_bringToFrontListener is { IsAlive: true })
                {
                    // Wait briefly to let the thread exit cleanly
                    _bringToFrontListener.Join(millisecondsTimeout: 200);
                }
            }
            catch { /* ignore */ }

            try { _bringToFrontEvent?.Dispose(); } catch { }

            try
            {
                if (_ownsMutex) _singleInstanceMutex?.ReleaseMutex();
                _singleInstanceMutex?.Dispose();
            }
            catch { }

            base.OnExit(e);
        }

        // ===== Inactivity input hook implementation =====
        private void HookUserInput()
        {
            if (_inputHooked) return;

            try
            {
                InputManager.Current.PreProcessInput += OnPreProcessInput;
                _inputHooked = true;

            }
            catch
            {
                // best-effort: if hooking fails, app still runs
            }
        }

        private void UnhookUserInput()
        {
            if (!_inputHooked) return;

            try
            {
                InputManager.Current.PreProcessInput -= OnPreProcessInput;

            }
            catch
            {
                // swallow
            }
            finally
            {
                _inputHooked = false;
            }
        }

        private void OnPreProcessInput(object sender, PreProcessInputEventArgs e)
        {
            // We only treat these as "activity":
            // - Keyboard keydown
            // - Mouse button down
            // - Mouse wheel
            //
            // (All are inherently app-scoped because they are routed through WPF input for this process.)
            try
            {
                var input = e?.StagingItem?.Input;
                if (input is null) return;

                if (input is KeyEventArgs keyArgs)
                {
                    if (keyArgs.RoutedEvent == Keyboard.KeyDownEvent)
                    {
                        UserActivityDetected?.Invoke();
                    }
                    return;
                }

                if (input is MouseButtonEventArgs mouseBtnArgs)
                {
                    if (mouseBtnArgs.RoutedEvent == Mouse.MouseDownEvent)
                    {
                        UserActivityDetected?.Invoke();
                    }
                    return;
                }

                if (input is MouseWheelEventArgs wheelArgs)
                {
                    UserActivityDetected?.Invoke();
                    return;
                }
            }
            catch
            {
                // swallow: never let input hook crash the app
            }
        }

        // === Bring-to-front plumbing ===
        private void BringToFrontListenLoop()
        {
            try
            {
                var evt = _bringToFrontEvent;
                if (evt is null) return;

                while (true)
                {
                    try
                    {
                        if (!evt.WaitOne()) continue;
                    }
                    catch (ThreadInterruptedException) { break; }
                    catch (ObjectDisposedException) { break; }

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            if (Current?.MainWindow is Window w)
                                BringWindowToFront(w);
                        }
                        catch (Exception ex)
                        {
                            try { EarlyLoginFailures.Write("App", "BringToFront handler exception", ex: ex); } catch { }
                        }
                    }));
                }
            }
            catch (Exception ex)
            {
                try { EarlyLoginFailures.Write("App", "BringToFront listener loop exception", ex: ex); } catch { }
            }
        }

        private static void BringWindowToFront(Window w)
        {
            if (w.WindowState == WindowState.Minimized)
                w.WindowState = WindowState.Normal;

            w.Activate();
            w.Topmost = true;  // nudge
            w.Topmost = false;
            w.Focus();

            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    ShowWindow(hwnd, SW_RESTORE);
                    SetForegroundWindow(hwnd);
                }
            }
            catch { /* ignore */ }
        }

        private static void ForceFatalShutdown()
        {
            try
            {
                if (Current?.Dispatcher != null && !Current.Dispatcher.CheckAccess())
                {
                    Current.Dispatcher.Invoke(() => AppExit.Shutdown(Current, AppExitCode.UnhandledFatalError, "Forced fatal shutdown."));
                    return;
                }

                AppExit.Shutdown(Current, AppExitCode.UnhandledFatalError, "Forced fatal shutdown.");
            }
            catch
            {
                RunSensitiveShutdownCleanupFatalLastDitch();
                Environment.Exit((int)AppExitCode.UnhandledFatalError);
            }
        }

        internal static void RunSensitiveShutdownCleanupFatalLastDitch()
        {
            RunSensitiveShutdownCleanup();
        }

        private static void RunSensitiveShutdownCleanup()
        {
            if (Interlocked.Exchange(ref _sensitiveShutdownCleanupRan, 1) == 1)
                return;

            try
            {
                SensitiveClipboardService.Shared.ClearIfOwned();
            }
            catch
            {
                // Shutdown cleanup is best-effort and must never surface UI.
            }

            try
            {
                SensitiveDataCleaner.WipeAll();
            }
            catch
            {
                // Shutdown cleanup is best-effort and must never mask the original exit reason.
            }
        }

        // Win32 helpers
        private const int SW_RESTORE = 9;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}
