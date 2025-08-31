// App.xaml.cs — full file (WPF, .NET 8)
using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Runtime.InteropServices;
using SevenZip;                       // path configured via SevenZipHelper
using Utilities.Diagnostics;          // EarlyLoginFailures, EarlyLogIngestor
using Utilities.Helpers;              // SevenZipHelper, ErrorHandler
using MWPV.Services;                  // LogRepository
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

            // Prevent auto-shutdown while showing the modal entry dialog
            var originalShutdownMode = ShutdownMode;
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

            // ---- Initialize SevenZip native DLL early ----
            try
            {
                var explicitPath = Path.Combine(AppContext.BaseDirectory, "7z.dll");
                if (!SevenZipHelper.ConfigureLibraryPath(explicitPath))
                    throw new InvalidOperationException($"7-Zip native DLL not found or failed to load. Looked for: {explicitPath}");
#if DEBUG
                System.Diagnostics.Debug.WriteLine("[App] SevenZip USING: " + SevenZipHelper.GetConfiguredPath());
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

            // ---- Startup ingest of any pre-existing early logs ----
            try
            {
                Directory.CreateDirectory(EarlyLoginFailures.StoreDir);
                Directory.CreateDirectory(EarlyLoginFailures.QuarantineDir);

                var startupRepo = new LogRepository();
                EarlyLogIngestor.IngestAll(startupRepo);
            }
            catch (Exception ex)
            {
                try { EarlyLoginFailures.Write("EarlyIngestor", "Failed during startup ingest", ex: ex); } catch { }
            }

            // ---- Entry flow (modal) -> then MainWindow ----
            try
            {
                var entry = new AppEntryWindow();
                var ok = entry.ShowDialog() == true;   // modal; DialogResult is valid inside
                if (!ok)
                {
                    Shutdown();
                    return;
                }

                // Heads-up if prior invalid attempts exist (same run or earlier)
                int pending = 0;
                try
                {
                    var earlyDir = EarlyLoginFailures.StoreDir;
                    if (Directory.Exists(earlyDir))
                        pending = Directory.GetFiles(earlyDir, "*.elogp", SearchOption.TopDirectoryOnly).Length;
                }
                catch { /* best-effort */ }

                if (pending > 0)
                {
                    ErrorHandler.InfoTitled(
                        "Previous Invalid Logins Detected",
                        $"{pending} prior invalid login attempt(s) were found.\n\n" +
                        "They will be ingested into the audit log now.",
                        "EarlyLoginNotice");
                }

                // Post-login ingest to catch files created during THIS run (e.g., bad pw then good)
                try
                {
                    var postRepo = new LogRepository();
                    EarlyLogIngestor.IngestAll(postRepo);
                }
                catch (Exception ex)
                {
                    try { EarlyLoginFailures.Write("EarlyIngestor", "Post-login ingest failed", ex: ex); } catch { }
                }

                var main = new MainWindow();
                MainWindow = main;
                main.Show();
            }
            catch (Exception ex)
            {
                try { EarlyLoginFailures.Write("App", "Failed to show initial window(s)", ex: ex); } catch { }
                Shutdown(-1);
                return;
            }
            finally
            {
                // Back to normal once MainWindow exists
                ShutdownMode = originalShutdownMode == ShutdownMode.OnExplicitShutdown
                    ? ShutdownMode.OnMainWindowClose
                    : originalShutdownMode;
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
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
                // snapshot to avoid races with field being nulled/disposed
                var evt = _bringToFrontEvent;
                if (evt is null) return;

                while (true)
                {
                    try
                    {
                        if (!evt.WaitOne()) continue;   // blocks until signaled
                    }
                    catch (ThreadInterruptedException) { break; }   // normal shutdown
                    catch (ObjectDisposedException) { break; }   // event disposed during shutdown

                    // marshal to UI thread
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
