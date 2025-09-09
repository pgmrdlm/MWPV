using System.Windows;
using System.Windows.Controls;

namespace MWPV.View.UserControls
{
    public partial class MenuBar : UserControl
    {
        public MenuBar()
        {
            InitializeComponent();
        }

        // Wire your XAML like:
        // <MenuItem Header="_Logs" Click="mnuToolsViewLogs_Click"/>
        private void mnuToolsViewLogs_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MWPV.MainWindow mw)
            {
                mw.ShowLogsPanel();
            }
        }
    }
}
