using System.Windows;
using System.Windows.Input;

namespace MWPV
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        // ---------------- Logs overlay bridge ----------------
        // Called by MenuBar (Tools -> View Logs)
        public void ShowLogsPanel()
        {
            try { Panel?.ShowLogs(); } catch { /* no-op */ }
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
