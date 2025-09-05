using System;
using System.Windows;
using System.Windows.Controls;
// REMOVE: using MWPV.View.Windows;
using MWPV.View.Logs; // optional, makes the ctor shorter

namespace MWPV.View.UserControls
{
    /// <summary>
    /// Interaction logic for MenuBar.xaml
    /// </summary>
    public partial class MenuBar : UserControl
    {
        public MenuBar()
        {
            InitializeComponent();
        }

        private void mnuToolsViewLogs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new ViewLogs();   // this is now a Window
                dlg.Owner = Application.Current?.MainWindow;
                dlg.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Couldn't open the Logs viewer.\n\n" + ex.Message,
                    "View Logs",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
