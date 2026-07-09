// File: MainWindow.xaml.cs
//
// FULL REWRITE
//
// Change made:
// - In inactivity timeout path, call Panel.ForceCancelActiveEditor_BestEffort() (your “press Cancel” path),
//   NOT PrepareForHostClose().
// - After that, TERMINATE THE APPLICATION (Close main window so existing Closing cleanup runs).
// - Everything else preserved as-is (close cleanup + status + lockdown + logs).

using System;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MWPV.Services;          // InactivityLockService
using Utilities.Helpers;

namespace MWPV
{
    public partial class MainWindow : Window
    {
        // ---- Status line auto-hide timer ----
        private readonly DispatcherTimer _statusTimer = new DispatcherTimer();
        private static readonly TimeSpan StatusInputClearDelay = TimeSpan.FromSeconds(3);
        private IDisposable? _statusMessageSubscription;
        private bool _statusClearableByInput = true;
        private DateTime _statusShownUtc = DateTime.MinValue;

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
            Panel.NavigationLockChanged += Panel_NavigationLockChanged;

            Title = "MWPV - My Windows Password Vault";
            LoadVersionDisplay();

            PreviewKeyDown += (_, __) => TryClearStatusFromUserInput();
            PreviewMouseDown += (_, __) => TryClearStatusFromUserInput();

            _statusTimer.Tick += (_, __) =>
            {
                if (!_uiLockedDown)
                    ClearStatus();
            };

            Closing += MainWindow_Closing;
            Loaded += (_, __) => ApplyMaximizedWorkArea();
            StateChanged += (_, __) => ApplyMaximizedWorkArea();
            _statusMessageSubscription = AppStatusMessageService.Subscribe(OnAppStatusMessagePublished);

            // ============================================================
            // Inactivity timer wiring (terminate app on timeout)
            // ============================================================

            _inactivity = new InactivityLockService(
                timeout: TimeSpan.FromMinutes(5),

                // Sensitive context = editor overlay is open (regardless of which tab is selected)
                isSensitiveContextOpen: () => Panel != null && Panel.IsEditorOverlayActive,

                // Reuse the EXISTING Cancel path (same as user clicking Cancel)
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

                // Final action: terminate the application (via normal close path)
                lockAction: () =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            // Prefer Close() so MainWindow_Closing cleanup runs.
                            Close();
                        }
                        catch (Exception ex)
                        {
                            try
                            {
                                _ = FatalErrorPopupHelper.ShowFatalAsync(
                                    "MWPV encountered a fatal error while closing after inactivity timeout and must close.",
                                    ex,
                                    "Main window close failed during the inactivity shutdown path.");
                            }
                            catch
                            {
                                try { Application.Current?.Shutdown(); } catch { }
                            }
                        }
                    }),
                    DispatcherPriority.Background);
                });

            _inactivity.Start();
        }

        private void LoadVersionDisplay()
        {
            string appVersion =
                Assembly.GetEntryAssembly()?.GetName()?.Version?.ToString() ?? "dev";

            string dbVersion = "unknown";

            try
            {
                var currentDbVersion = DbVersionService.GetCurrentVersion();
                if (!string.IsNullOrWhiteSpace(currentDbVersion?.Version))
                    dbVersion = currentDbVersion.Version;
            }
            catch
            {
                dbVersion = "unknown";
            }

            VersionDisplayText.Text = $"App: {appVersion}  DB: {dbVersion}";
        }

        private void ApplyMaximizedWorkArea()
        {
            if (WindowState != WindowState.Maximized)
                return;

            var workArea = SystemParameters.WorkArea;
            MaxWidth = workArea.Width;
            MaxHeight = workArea.Height;
            Left = workArea.Left;
            Top = workArea.Top;
        }

        // ============================================================
        // LOCKDOWN API
        // ============================================================

        public void SetUiLockdown(bool locked, string? message = null)
        {
            _uiLockedDown = locked;

            TrySetMenuBarEnabled(!locked);
            if (MenuLockOverlay != null)
                MenuLockOverlay.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;

            if (locked)
            {
                ShowStartupStatus(message ?? DefaultLockdownMessage, autoHide: null);
            }
            else
            {
                ClearStatus();
            }
        }

        private void Panel_NavigationLockChanged(object? sender, MWPV.View.UserControls.Panel.NavigationLockChangedEventArgs e)
        {
            SetUiLockdown(e.IsLocked, e.Message);
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

        public void ShowAppSettingsPanel()
        {
            if (_uiLockedDown)
            {
                ShowStartupStatus(DefaultLockdownMessage, TimeSpan.FromSeconds(6));
                return;
            }

            try
            {
                Panel?.ShowAppSettings();
            }
            catch
            {
            }
        }

        // ============================================================
        // Status helpers
        // ============================================================

        public void ShowStartupStatus(string message, TimeSpan? autoHide = null)
            => ShowStatusMessage(message, autoHide, clearOnUserInput: true);

        private void OnAppStatusMessagePublished(AppStatusMessage message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnAppStatusMessagePublished(message)));
                return;
            }

            ShowStatusMessage(message.Text, message.AutoClearAfter, message.ClearOnUserInput);
        }

        private void ShowStatusMessage(string message, TimeSpan? autoHide, bool clearOnUserInput)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                ClearStatus();
                return;
            }

            StatusText.Text = message;
            StatusText.Visibility = Visibility.Visible;
            StatusBannerHost.Visibility = Visibility.Visible;
            _statusClearableByInput = clearOnUserInput;
            _statusShownUtc = DateTime.UtcNow;

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
            _statusClearableByInput = true;
            _statusShownUtc = DateTime.MinValue;

            if (StatusText.Visibility != Visibility.Collapsed)
            {
                StatusText.Visibility = Visibility.Collapsed;
                StatusText.Text = string.Empty;
            }

            if (StatusBannerHost.Visibility != Visibility.Visible)
                StatusBannerHost.Visibility = Visibility.Visible;
        }

        private void TryClearStatusFromUserInput()
        {
            if (_uiLockedDown || !_statusClearableByInput)
                return;

            if (_statusShownUtc == DateTime.MinValue)
                return;

            if (DateTime.UtcNow - _statusShownUtc < StatusInputClearDelay)
                return;

            ClearStatus();
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

            bool allowClose = true;
            try { allowClose = Panel?.TryHostClosePreflight_BestEffort() ?? true; } catch { }
            if (!allowClose)
            {
                e.Cancel = true;
                return;
            }

            _closingCleanupInProgress = true;
            e.Cancel = true;

            try
            {
                try { _statusMessageSubscription?.Dispose(); } catch { }
                _statusMessageSubscription = null;

                try { _inactivity?.Dispose(); } catch { }
                _inactivity = null;

                _uiLockedDown = false;
                ClearStatus();

                // Host close wipe path (separate from inactivity cancel path)
                try { Panel?.PrepareForHostClose(); } catch { }

                Content = null;

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        _allowCloseAfterCleanup = true;
                        Close();
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            _ = FatalErrorPopupHelper.ShowFatalAsync(
                                "MWPV encountered a fatal error while closing and must close.",
                                ex,
                                "Main window close failed after shutdown cleanup.");
                        }
                        catch
                        {
                            try { Application.Current.Shutdown(); } catch { }
                        }
                    }
                    finally
                    {
                        _closingCleanupInProgress = false;
                    }
                }),
                DispatcherPriority.ApplicationIdle);
            }
            catch (Exception ex)
            {
                _allowCloseAfterCleanup = true;
                _closingCleanupInProgress = false;

                try
                {
                    _ = FatalErrorPopupHelper.ShowFatalAsync(
                        "MWPV encountered a fatal error while closing and must close.",
                        ex,
                        "An exception occurred during main window shutdown cleanup.");
                }
                catch
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try { Close(); } catch { }
                    }),
                    DispatcherPriority.Background);
                }
            }
        }
    }
}
