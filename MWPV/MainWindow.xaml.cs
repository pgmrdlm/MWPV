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
using System.Threading.Tasks;
using MWPV.Services;          // InactivityLockService
using MWPV.Services.AppLifecycle;
using MWPV.Services.Security;
using MWPV.View.UserControls.Popup;
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
        private CloseState _closeState = CloseState.Idle;
        private bool _forcedCloseIntent;
        private readonly BackupOnExitCoordinator _backupOnExitCoordinator = new();
        private readonly LogPurgeCoordinator _logPurgeCoordinator = new();
        private bool _maintenanceInProgress;

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
            if (Application.Current != null)
                Application.Current.SessionEnding += (_, __) => _forcedCloseIntent = true;
            Loaded += (_, __) => ApplyMaximizedWorkArea();
            StateChanged += (_, __) => ApplyMaximizedWorkArea();
            _statusMessageSubscription = AppStatusMessageService.Subscribe(OnAppStatusMessagePublished);

            // ============================================================
            // Inactivity timer wiring (terminate app on timeout)
            // ============================================================

            _inactivity = new InactivityLockService(
                timeout: TimeSpan.FromMinutes(AppSettingsService.GetInactivityTimeoutMinutes()),

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
                            _forcedCloseIntent = true;
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

            AppSettingsService.InactivityTimeoutChanged += OnInactivityTimeoutChanged;
            _inactivity.Start();
        }

        private void OnInactivityTimeoutChanged(int minutes)
        {
            _inactivity?.UpdateTimeout(TimeSpan.FromMinutes(minutes));
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
                MenuBar.SetToolsNavigationEnabled(false);
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

        public async Task PurgeLogsAsync()
        {
            if (_maintenanceInProgress || _uiLockedDown)
                return;

            try
            {
                var preview = await _logPurgeCoordinator.GetPreviewAsync();
                if (preview.Total == 0)
                {
                    MessageBox.Show(
                        $"No session logs are old enough to purge.\n\nThe current retention setting is {preview.RetentionDays} days.",
                        "Nothing to Purge",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    AppStatusMessageService.Publish("No session logs are older than the retention cutoff.", AppStatusMessageKind.Info, TimeSpan.FromSeconds(8));
                    return;
                }

                string cutoffLocal = preview.CutoffUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz");
                var answer = MessageBox.Show(
                    $"Purge SESSION_START and SESSION_END logs older than {cutoffLocal}?\n\n" +
                    $"Retention: {preview.RetentionDays} days\nQualifying rows: {preview.Total}\n" +
                    $"SESSION_START: {preview.Starts}\nSESSION_END: {preview.Ends}\n\n" +
                    "A verified full vault backup will be created before deletion.",
                    "Purge retained session logs", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                if (answer != MessageBoxResult.OK)
                {
                    AppStatusMessageService.Publish("Session log purge canceled. No changes were made.", AppStatusMessageKind.Info, TimeSpan.FromSeconds(8));
                    return;
                }

                var result = await RunExclusiveLogPurgeAsync(stage => _logPurgeCoordinator.PurgeAsync(DateTimeOffset.UtcNow, stage));
                if (!result.Succeeded)
                {
                    AppStatusMessageService.Publish("Session log purge failed. No logs were deleted.", AppStatusMessageKind.Warning, TimeSpan.FromSeconds(10));
                    return;
                }
                if (result.Deleted.Total == 0)
                {
                    AppStatusMessageService.Publish("No session logs are older than the retention cutoff.", AppStatusMessageKind.Info, TimeSpan.FromSeconds(8));
                    return;
                }
                AppStatusMessageService.Publish($"Session log purge complete. Removed {result.Deleted.Starts} starts and {result.Deleted.Ends} ends. Verified backup created.", AppStatusMessageKind.Success, TimeSpan.FromSeconds(12));
            }
            catch
            {
                AppStatusMessageService.Publish("Session log purge failed. No logs were deleted.", AppStatusMessageKind.Warning, TimeSpan.FromSeconds(10));
            }
        }

        public void SetLogDisplayToolsNavigationEnabled(bool enabled)
        {
            if (!_uiLockedDown)
                MenuBar.SetToolsNavigationEnabled(enabled);
        }

        public async Task<T> RunExclusiveLogPurgeAsync<T>(Func<Action<string>, Task<T>> operation)
        {
            if (_maintenanceInProgress)
                throw new InvalidOperationException("Maintenance is already in progress.");
            _maintenanceInProgress = true;
            IsEnabled = false;
            MaintenanceOverlay.Visibility = Visibility.Visible;
            MaintenanceStageText.Text = "Creating and verifying backup before log purge...";
            SensitiveClipboardService.Shared.SuspendDatabaseActivity();
            try
            {
                await BackgroundDatabaseActivityGate.SuppressAndWaitForIdleAsync();
                return await operation(stage => Dispatcher.Invoke(() => MaintenanceStageText.Text = stage));
            }
            finally
            {
                BackgroundDatabaseActivityGate.Resume();
                SensitiveClipboardService.Shared.ResumeDatabaseActivity();
                MaintenanceOverlay.Visibility = Visibility.Collapsed;
                IsEnabled = true;
                _maintenanceInProgress = false;
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

        private async void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (_maintenanceInProgress)
            {
                e.Cancel = true;
                return;
            }
            if (_closeState == CloseState.CloseApproved)
                return;

            e.Cancel = true;

            if (_closeState != CloseState.Idle)
            {
                return;
            }

            bool allowClose = true;
            try { allowClose = Panel?.TryHostClosePreflight_BestEffort() ?? true; } catch { }
            if (!allowClose)
            {
                _closeState = CloseState.Idle;
                return;
            }

            if (ShouldBypassBackupPrompt() ||
                !VaultSessionStateService.VaultDataChangedThisSession ||
                !AppSettingsService.GetBackupPromptOnExitAfterChanges())
            {
                BeginFinalCleanup();
                return;
            }

            _closeState = CloseState.PromptActive;
            PopupDialog.PopupResult decision;
            try
            {
                var popup = new PopupDialog
                {
                    EnterResult = PopupDialog.PopupResult.Accept,
                    InitialFocusResult = PopupDialog.PopupResult.Accept
                };
                popup.ConfigureThreeActions(
                    severity: 0,
                    title: "Backup Before Closing",
                    message: "Changes were made to your vault during this session. Would you like to create a backup before closing?",
                    primaryText: "Create Backup and Close",
                    alternateText: "Close Without Backup",
                    cancelText: "Cancel");

                if (Panel == null)
                {
                    _closeState = CloseState.Idle;
                    return;
                }

                decision = await Panel.ShowPopupAsync(popup);
            }
            catch
            {
                _closeState = CloseState.Idle;
                return;
            }

            if (decision == PopupDialog.PopupResult.Cancel || decision == PopupDialog.PopupResult.Abort)
            {
                _closeState = CloseState.Idle;
                return;
            }

            if (decision == PopupDialog.PopupResult.Alternate)
            {
                BeginFinalCleanup();
                return;
            }

            await CreateBackupAndCloseAsync();
        }

        private bool ShouldBypassBackupPrompt() =>
            _forcedCloseIntent ||
            AppExit.FinalCode != AppExitCode.Success ||
            AppRunState.StartupContext.RunMode == AppRunMode.Upgrade;

        private async Task CreateBackupAndCloseAsync()
        {
            _closeState = CloseState.BackupRunning;
            IsEnabled = false;
            SensitiveClipboardService.Shared.SuspendDatabaseActivity();

            try
            {
                await BackgroundDatabaseActivityGate.SuppressAndWaitForIdleAsync();
                var result = await _backupOnExitCoordinator.CreateVerifiedBackupAsync();
                if (!result.Succeeded)
                {
                    RestoreAfterBackupFailure();
                    MessageBox.Show(
                        "MWPV could not create and verify the backup. The application will remain open. Check that sufficient storage is available and try closing again.",
                        "Backup Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                BeginFinalCleanup();
            }
            catch
            {
                RestoreAfterBackupFailure();
                MessageBox.Show(
                    "MWPV could not create and verify the backup. The application will remain open. Check that sufficient storage is available and try closing again.",
                    "Backup Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void RestoreAfterBackupFailure()
        {
            BackgroundDatabaseActivityGate.Resume();
            SensitiveClipboardService.Shared.ResumeDatabaseActivity();
            IsEnabled = true;
            _closeState = CloseState.Idle;
        }

        private void BeginFinalCleanup()
        {
            if (_closeState == CloseState.CleanupRunning || _closeState == CloseState.CloseApproved)
                return;

            _closeState = CloseState.CleanupRunning;

            try
            {
                try { _statusMessageSubscription?.Dispose(); } catch { }
                _statusMessageSubscription = null;

                try { _inactivity?.Dispose(); } catch { }
                AppSettingsService.InactivityTimeoutChanged -= OnInactivityTimeoutChanged;
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
                        _closeState = CloseState.CloseApproved;
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
                }),
                DispatcherPriority.ApplicationIdle);
            }
            catch (Exception ex)
            {
                _closeState = CloseState.CloseApproved;

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

        private enum CloseState
        {
            Idle,
            PromptActive,
            BackupRunning,
            CleanupRunning,
            CloseApproved
        }
    }
}
