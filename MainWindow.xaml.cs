using System.Windows;
using Utilities.Helpers;
using Utilities.Helpers.Debug;


#if DEBUG
using System;
using System.Windows.Input;
using Microsoft.Data.Sqlite;
using Utilities.Helpers.Debug;       // DebugCommands
using Utilities.Services;    // FullSqlExportService
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
    }
}
