// App.xaml.cs — full file (WPF, .NET 8)
using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Runtime.InteropServices;
using System.Windows.Controls;         // ✅ for TextBlock (StatusText)
using Utilities.Diagnostics;           // EarlyLoginFailures, EarlyLogIngestor
using Utilities.Helpers;               // ErrorHandler
using Utilities.Security;
using Security.Utility.Archives;       // ✅ SevenZipCore
using MWPV.Services;                   // ✅ LogCatalogService (for SESSION_END)

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

        protected override void OnStartup(StartupEventArgs e)
        {
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

            // ---- Initialize SevenZip native DLL early (centralized) ----
            try
            {
                var explicitPath = Path.Combine(AppContext.BaseDirectory, "7z.dll");
                var (ok, reason) = SevenZipCore.EnsureConfigured(explicitPath);
                if (!ok)
                    throw new InvalidOperationException(
                        $"7-Zip native DLL not found or failed to load. Looked for: {explicitPath}. {reason}");

#if DEBUG
                System.Diagnostics.Debug.WriteLine("[App] SevenZip USING: " + (SevenZipCore.GetConfiguredPath() ?? "<null>"));
#endif
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(
                    ex,
                    "SevenZip could not be initialized.\n(Check that 7z.dll matches process bitness and is present.)",
                    "SevenZip.Init");
                Shutdown();
                return;
            }

            // ---- Early log folders only (no ingest before login) ----
            try
            {
                Directory.CreateDirectory(EarlyLoginFailures.StoreDir);
                Directory.CreateDirectory(EarlyLoginFailures.QuarantineDir);
                // NOTE: We intentionally DO NOT call EarlyLogIngestor.IngestAll() here.
                // Ingest runs ONLY after a successful key/database login.
#if DEBUG
                System.Diagnostics.Debug.WriteLine("[EarlyIngest] Startup: skipping ingest until post-login.");
#endif
            }
            catch (Exception ex)
            {
                try { EarlyLoginFailures.Write("EarlyIngestor", "Failed to ensure early log directories", ex: ex); } catch { }
            }

            // ===== Abnormal-exit hooks (best-effort) =====
            // Only log SESSION_END if a valid login occurred this run.
            AppDomain.CurrentDomain.UnhandledException += (_, __) =>
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
            };

            this.DispatcherUnhandledException += (_, __) =>
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
            };
            // =============================================

            // ---- Entry flow (modal) -> then MainWindow ----
            try
            {
                var entry = new AppEntryWindow();
                var ok = entry.ShowDialog() == true;   // modal; null/false if closed/cancelled
                if (!ok)
                {
                    Shutdown();        // EXIT CLEANLY ON CANCEL/CLOSE
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

                // Quiet one-line status to show after MainWindow is up (only if there were pending files)
                string? startupStatus = null;
                if (pending > 0)
                    startupStatus = $"{pending} prior invalid login attempt(s) were ingested into the audit log.";

                // Create and show main window, THEN restore normal shutdown behavior
                if (!Dispatcher.HasShutdownStarted)
                {
                    var main = new MainWindow();
                    MainWindow = main;
                    ShutdownMode = ShutdownMode.OnMainWindowClose;  // <-- set ONLY after MainWindow exists
                    main.Show();

                    // ✅ CHANGE: route through MainWindow helper so it auto-hides & clears on input
                    if (!string.IsNullOrWhiteSpace(startupStatus))
                    {
                        try
                        {
                            (main as MainWindow)?.ShowStartupStatus(startupStatus, TimeSpan.FromSeconds(8));
                        }
                        catch { /* best-effort; if MainWindow not ready, silently ignore */ }
                    }
                }
            }
            catch (Exception ex)
            {
                try { EarlyLoginFailures.Write("App", "Failed to show initial window(s)", ex: ex); } catch { }
                Shutdown(-1);
                return;
            }
            // NOTE: No 'finally' that touches ShutdownMode — avoids the crash when app is already shutting down.
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

        // Win32 helpers
        private const int SW_RESTORE = 9;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}
