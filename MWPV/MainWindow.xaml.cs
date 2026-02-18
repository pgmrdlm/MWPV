// File: MainWindow.xaml.cs
//
// FULL REWRITE
//
// Change made:
// - In inactivity timeout path, call Panel.ForceCancelActiveEditor_BestEffort() (your “press Cancel” path),
//   NOT PrepareForHostClose().
// - Everything else preserved as-is (close cleanup + status + lockdown + logs).

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MWPV.Services;          // InactivityLockService

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

        // ---- Inactivity lock service ----
        private InactivityLockService? _inactivity;

        // Optional: standard message shown during editor lockdown
        private const string DefaultLockdownMessage =
            "Navigation is disabled while an item is open. Close the editor (Save/Cancel) to continue.";

        public MainWindow()
        {
            InitializeComponent();

            Title = "MWPV - My Windows Password Vault";

            PreviewKeyDown += (_, __) => { if (!_uiLockedDown) ClearStatus(); };
            PreviewMouseDown += (_, __) => { if (!_uiLockedDown) ClearStatus(); };

            _statusTimer.Tick += (_, __) =>
            {
                if (!_uiLockedDown)
                    ClearStatus();
            };

            Closing += MainWindow_Closing;

            // ============================================================
            // Inactivity timer wiring (FINAL CORRECT VERSION)
            // ============================================================

            _inactivity = new InactivityLockService(
                timeout: TimeSpan.FromMinutes(5),

                // Sensitive context = editor overlay is open (regardless of which tab is selected)
                // Requirement: after 5 minutes of inactivity, wipe + close the editor (Cancel path).
                isSensitiveContextOpen: () => Panel != null && Panel.IsEditorOverlayActive,

                // IMPORTANT:
                // - This must reuse the EXISTING Cancel path (same as user clicking Cancel)
                // - Not a wipe-only host-close
                forceCancelSensitiveContext: () =>
                {
                    try
                    {
                        Panel?.ForceCancelActiveEditor_BestEffort();
                    }
                    catch
                    {
                        // swallow
                    }
                },

                // Placeholder until Hello gate UI is wired
                lockAction: () =>
                {
                    ShowStartupStatus(
                        "Locked due to inactivity. (Hello gate not wired yet.)",
                        autoHide: null);
                });

            _inactivity.Start();
        }

        // ============================================================
        // LOCKDOWN API
        // ============================================================

        public void SetUiLockdown(bool locked, string? message = null)
        {
            _uiLockedDown = locked;

            TrySetMenuBarEnabled(!locked);

            if (locked)
            {
                ShowStartupStatus(message ?? DefaultLockdownMessage, autoHide: null);
            }
            else
            {
                ClearStatus();
            }
        }

        private void TrySetMenuBarEnabled(bool enabled)
        {
            try
            {
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
        // Logs bridge
        // ============================================================

        public void ShowLogsPanel()
        {
            if (_uiLockedDown)
            {
                ShowStartupStatus(DefaultLockdownMessage, TimeSpan.FromSeconds(6));
                return;
            }

            try
            {
                Panel?.ShowLogs();
            }
            catch
            {
            }
        }

        // ============================================================
        // Status helpers
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
                MaxRestore_Click(sender, e);
                return;
            }

            try { DragMove(); }
            catch { }
        }

        private void TitleBar_OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaxRestore_Click(object sender, RoutedEventArgs e)
        {
            WindowState =
                WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // ============================================================
        // Close cleanup
        // ============================================================

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
                Debug.WriteLine("[MAINWINDOW][CLOSE] Cleanup starting.");

                try { _inactivity?.Dispose(); } catch { }
                _inactivity = null;

                _uiLockedDown = false;
                ClearStatus();

                // Host close still does wipe path (separate from inactivity cancel path)
                try { Panel?.PrepareForHostClose(); } catch { }

                Content = null;

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        _allowCloseAfterCleanup = true;
                        Close();
                    }
                    catch
                    {
                        try { Application.Current.Shutdown(); } catch { }
                    }
                    finally
                    {
                        _closingCleanupInProgress = false;
                    }
                }),
                DispatcherPriority.ApplicationIdle);
            }
            catch
            {
                _allowCloseAfterCleanup = true;
                _closingCleanupInProgress = false;

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { Close(); } catch { }
                }),
                DispatcherPriority.Background);
            }
        }
    }
}
