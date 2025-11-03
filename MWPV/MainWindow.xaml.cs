using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace MWPV
{
    public partial class MainWindow : Window
    {
        // ---- Status line auto-hide timer ----
        private readonly DispatcherTimer _statusTimer = new DispatcherTimer();

        public MainWindow()
        {
            InitializeComponent();

            // Clear the status on any user input
            this.PreviewKeyDown += (_, __) => ClearStatus();
            this.PreviewMouseDown += (_, __) => ClearStatus();

            // Timer tick clears the status
            _statusTimer.Tick += (_, __) => ClearStatus();
        }

        // ---------------- Logs overlay bridge ----------------
        // Called by MenuBar (Tools -> View Logs)
        public void ShowLogsPanel()
        {
            try { Panel?.ShowLogs(); } catch { /* no-op */ }
        }

        // ---------------- Status line helpers ----------------
        /// <summary>
        /// Show a one-line startup/status message and auto-hide after the given duration.
        /// Pass null/empty to clear immediately. Default auto-hide = 8 seconds.
        /// </summary>
        public void ShowStartupStatus(string message, TimeSpan? autoHide = null)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                ClearStatus();
                return;
            }

            // StatusText is defined in MainWindow.xaml (TextBlock)
            StatusText.Text = message;
            StatusText.Visibility = Visibility.Visible;

            _statusTimer.Stop();
            var delay = autoHide ?? TimeSpan.FromSeconds(8);
            _statusTimer.Interval = delay;
            _statusTimer.Start();
        }

        /// <summary>
        /// Hide the status line and stop any pending auto-hide.
        /// </summary>
        private void ClearStatus()
        {
            _statusTimer.Stop();
            if (StatusText.Visibility != Visibility.Collapsed)
            {
                StatusText.Visibility = Visibility.Collapsed;
                StatusText.Text = string.Empty;
            }
        }

        // ---------------- Title bar handlers (no visual changes) ----------------
        private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Allow dragging the window; double-click toggles maximize/restore
            if (e.ClickCount == 2)
            {
                MaxRestore_Click(sender, e);
            }
            else
            {
                try { DragMove(); } catch { /* ignore if drag starts during resize */ }
            }
        }

        private void TitleBar_OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Intentionally no-op to avoid changing your menu/title bar behavior.
            // (If you later want a system menu here, we can add it without visual changes.)
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

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
