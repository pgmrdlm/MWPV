using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace MWPV
{
    public partial class MainWindow : Window
    {
        // ---- Status line auto-hide timer ----
        private readonly DispatcherTimer _statusTimer = new DispatcherTimer();

        // ---- Close orchestration ----
        private bool _closingCleanupInProgress;
        private bool _allowCloseAfterCleanup;

        public MainWindow()
        {
            InitializeComponent();

            // Set the correct window title explicitly on load
            Title = "MWPV - My Windows Password Vault";

            // Clear the status on any user input
            PreviewKeyDown += (_, __) => ClearStatus();
            PreviewMouseDown += (_, __) => ClearStatus();

            // Timer tick clears the status
            _statusTimer.Tick += (_, __) => ClearStatus();

            // Central place to handle the big X / Alt+F4 / system close
            Closing += MainWindow_Closing;
        }

        // ---------------- Logs overlay bridge ----------------
        // Called by MenuBar (Tools -> View Logs)
        public void ShowLogsPanel()
        {
            try
            {
                Panel?.ShowLogs();
            }
            catch
            {
                // no-op
            }
        }

        // ---------------- Status line helpers ----------------
        public void ShowStartupStatus(string message, TimeSpan? autoHide = null)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                ClearStatus();
                return;
            }

            StatusText.Text = message;
            StatusText.Visibility = Visibility.Visible;

            _statusTimer.Stop();
            _statusTimer.Interval = autoHide ?? TimeSpan.FromSeconds(8);
            _statusTimer.Start();
        }

        private void ClearStatus()
        {
            _statusTimer.Stop();

            if (StatusText.Visibility != Visibility.Collapsed)
            {
                StatusText.Visibility = Visibility.Collapsed;
                StatusText.Text = string.Empty;
            }
        }

        // ---------------- Title bar handlers ----------------
        private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                MaxRestore_Click(sender, e); // MouseButtonEventArgs derives from RoutedEventArgs
                return;
            }

            try { DragMove(); }
            catch { /* ignore */ }
        }

        private void TitleBar_OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            // no-op (by design)
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaxRestore_Click(object sender, RoutedEventArgs e)
        {
            WindowState = (WindowState == WindowState.Maximized)
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        // Big X in our custom title bar
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close(); // goes through Closing handler below
        }

        /// <summary>
        /// Ensure Unloaded runs for nested controls BEFORE we actually exit.
        /// Critical: DO NOT call Close() from inside Closing (WPF will throw).
        /// We cancel, detach content, then schedule the real close after Closing returns.
        /// </summary>
        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (_allowCloseAfterCleanup)
                return;

            if (_closingCleanupInProgress)
            {
                e.Cancel = true;
                return;
            }

            _closingCleanupInProgress = true;
            e.Cancel = true;

            try
            {
                Debug.WriteLine("[MAINWINDOW][CLOSE] Closing requested — beginning UI unload cleanup.");

                // Stop status timer + hide status (safe during close)
                ClearStatus();

                // **CRITICAL**: tell the active editor (and its children) to run host-close wipes
                // BEFORE we rip the visual tree out (Content = null).
                try { Panel?.PrepareForHostClose(); } catch { /* no-op */ }

                // Trigger Unloaded down the tree (Panel, ItemTabs, BankCards, etc.)
                Content = null;

                // Schedule the final close AFTER this Closing handler returns.
                // Use idle-ish priority so Unloaded handlers get a chance to run first.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        Debug.WriteLine("[MAINWINDOW][CLOSE] UI unload cleanup pass complete. Proceeding to close.");

                        _allowCloseAfterCleanup = true;

                        // Now it's safe to close (new close pass, we won't cancel it)
                        Close();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("[MAINWINDOW][CLOSE] Final-close exception: " + ex);

                        // Last resort: shut down the app to avoid hanging on a blank window
                        try { Application.Current.Shutdown(); } catch { /* ignore */ }
                    }
                    finally
                    {
                        _closingCleanupInProgress = false;
                    }
                }), DispatcherPriority.ApplicationIdle);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[MAINWINDOW][CLOSE] Cleanup exception: " + ex);

                // If cleanup setup failed, allow close immediately (don’t strand a blank window)
                _allowCloseAfterCleanup = true;
                _closingCleanupInProgress = false;

                // Schedule close outside Closing
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { Close(); } catch { /* ignore */ }
                }), DispatcherPriority.Background);
            }
        }
    }
}
