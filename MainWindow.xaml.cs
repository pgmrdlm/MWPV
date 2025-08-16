// MainWindow.xaml.cs — full rewrite (adds only title bar plumbing; keeps your original logic)
using System;
using System.Windows;
using System.Windows.Input;
using Utilities.Helpers;
using Utilities.Helpers.Debug;

#if DEBUG
using Microsoft.Data.Sqlite;
using Utilities.Services; // FullSqlExportService
#endif

namespace MWPV
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            this.Visibility = Visibility.Visible;
            this.WindowState = WindowState.Normal;
            // Refresh is now handled by Panel

            // Title bar glyph sync (no override of OnStateChanged to keep Hot Reload happy)
            UpdateMaxRestoreGlyph();
            this.StateChanged += (_, __) => UpdateMaxRestoreGlyph();

#if DEBUG
            try
            {
                // Ctrl+Alt+L -> Export FULL DB to .sql (unencrypted; Logs.Payload decrypted to JSON)
                InputBindings.Add(new KeyBinding(
                    DebugCommands.ExportFullSql,
                    new KeyGesture(Key.L, ModifierKeys.Control | ModifierKeys.Alt)));

                CommandBindings.Add(new CommandBinding(DebugCommands.ExportFullSql, async (_, __) =>
                {
                    Func<SqliteConnection> openAppConn = DatabaseHelper.GetAppOpenConnection;
                    await FullSqlExportService.ExportFullDbAsSqlAsync(openAppConn, decryptLogsPayload: true);
                }));
            }
            catch { /* keep constructor bulletproof */ }
#endif
        }

        private void OpenCategoryEntry_Click(object sender, RoutedEventArgs e)
        {
            var categoryWindow = new NewCategoryEntry(this);
            categoryWindow.Owner = this;
            categoryWindow.ShowDialog();

            if (categoryWindow.DialogResult == true)
            {
                Panel.RefreshCategoryGrid();
            }
        }

        // ===== Title Bar Handlers =====
        private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && e.LeftButton == MouseButtonState.Pressed)
            {
                ToggleMaxRestore();
                return;
            }

            if (e.ButtonState == MouseButtonState.Pressed)
            {
                // Smart drag from maximized: restore near cursor then drag
                if (WindowState == WindowState.Maximized)
                {
                    var mouse = PointToScreen(e.GetPosition(this));
                    WindowState = WindowState.Normal;
                    Left = mouse.X - (ActualWidth * 0.5);
                    Top = Math.Max(mouse.Y - 20, 0);
                }
                try { DragMove(); } catch { /* ignore if mouse released */ }
            }
        }

        private void TitleBar_OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            var screen = PointToScreen(e.GetPosition(this));
            SystemCommands.ShowSystemMenu(this, screen);
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
            => SystemCommands.MinimizeWindow(this);

        private void MaxRestore_Click(object sender, RoutedEventArgs e)
            => ToggleMaxRestore();

        private void Close_Click(object sender, RoutedEventArgs e)
            => SystemCommands.CloseWindow(this);

        private void ToggleMaxRestore()
        {
            if (WindowState == WindowState.Maximized)
                SystemCommands.RestoreWindow(this);
            else
                SystemCommands.MaximizeWindow(this);

            UpdateMaxRestoreGlyph();
        }

        private void UpdateMaxRestoreGlyph()
        {
            // Flip the MDL2 glyph shown inside the Max/Restore button
            if (TbMaxGlyph != null)
            {
                TbMaxGlyph.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
            }
            if (MaxRestoreButton != null)
            {
                MaxRestoreButton.ToolTip = WindowState == WindowState.Maximized ? "Restore" : "Maximize";
            }
        }
    }
}
