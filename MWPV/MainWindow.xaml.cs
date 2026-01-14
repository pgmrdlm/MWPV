// File: MainWindow.xaml.cs
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
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

        // ---- UI Lockdown (editor open) ----
        private bool _uiLockedDown;

        // Optional: standard message shown during editor lockdown
        private const string DefaultLockdownMessage =
            "Navigation is disabled while an item is open. Close the editor (Save/Cancel) to continue.";

        public MainWindow()
        {
            InitializeComponent();

            // Set the correct window title explicitly on load
            Title = "MWPV - My Windows Password Vault";

            // Clear the status on any user input (unless locked down)
            PreviewKeyDown += (_, __) => { if (!_uiLockedDown) ClearStatus(); };
            PreviewMouseDown += (_, __) => { if (!_uiLockedDown) ClearStatus(); };

            // Timer tick clears the status (unless locked down)
            _statusTimer.Tick += (_, __) =>
            {
                if (!_uiLockedDown)
                    ClearStatus();
            };

            // Central place to handle the big X / Alt+F4 / system close
            Closing += MainWindow_Closing;
        }

        // ============================================================
        // LOCKDOWN API (called by Panel via event)
        // ============================================================

        /// <summary>
        /// Central lockdown switch:
        /// - Disables menu bar (and anything else we decide later).
        /// - Shows a persistent banner while locked.
        /// - Keeps Big X working (window close).
        /// </summary>
        public void SetUiLockdown(bool locked, string? message = null)
        {
            _uiLockedDown = locked;

            // 1) Disable the menu bar while locked (Tools/File/etc)
            TrySetMenuBarEnabled(!locked);

            // 2) Show a persistent banner while locked
            if (locked)
            {
                ShowStartupStatus(message ?? DefaultLockdownMessage, autoHide: null);
            }
            else
            {
                ClearStatus();
            }

//#if DEBUG
//            Debug.WriteLine($"[MAINWINDOW][LOCKDOWN] locked={locked}");
//#endif
        }

        private void TrySetMenuBarEnabled(bool enabled)
        {
            try
            {
                // MenuBar is placed in XAML as <userControls:MenuBar Grid.Row="1"/>
                // It has no x:Name in your posted XAML, so we locate it by type.
                var menu = FindChildByType<MWPV.View.UserControls.MenuBar>(this);
                if (menu != null)
                    menu.IsEnabled = enabled;
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine("[MAINWINDOW][LOCKDOWN][WARN] Failed to toggle MenuBar: " + ex);
#endif
            }
        }

        private static T? FindChildByType<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) return null;

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T typed)
                    return typed;

                var nested = FindChildByType<T>(child);
                if (nested != null)
                    return nested;
            }

            return null;
        }

        // ============================================================
        // Logs overlay bridge
        // ============================================================

        // Called by MenuBar (Tools -> View Logs)
        public void ShowLogsPanel()
        {
            // Hard guard: if editor is open, we do not allow logs.
            if (_uiLockedDown)
            {
                ShowStartupStatus(DefaultLockdownMessage, autoHide: TimeSpan.FromSeconds(6));
                return;
            }

            try
            {
                Panel?.ShowLogs();
            }
            catch
            {
                // no-op
            }
        }

        // ============================================================
        // Status line helpers
        // ============================================================

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

            // If autoHide is null: persist until caller clears it.
            if (autoHide.HasValue)
            {
                _statusTimer.Interval = autoHide.Value;
                _statusTimer.Start();
            }
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

        // ============================================================
        // Title bar handlers
        // ============================================================

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

        // ============================================================
        // Close cleanup orchestration
        // ============================================================

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
                _uiLockedDown = false; // no need to keep UI locked while closing
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
