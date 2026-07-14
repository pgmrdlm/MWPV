using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace MWPV.View.UserControls
{
    public partial class MenuBar : UserControl
    {
        public MenuBar()
        {
            InitializeComponent();
        }

        // NOTE: In XAML, set:
        //   Placement="Custom"
        //   CustomPopupPlacementCallback="OnPopupPlacement"
        //   IsOpen="{TemplateBinding IsSubmenuOpen}"
        //
        // This callback right-aligns the submenu to its parent header and drops it directly below.
        // We use a tiny inset from the right edge to soften the visual.
        private const double RightInset = 6.0; // set to 0 for perfectly flush alignment

        private static CustomPopupPlacement[] OnPopupPlacement(Size popupSize, Size targetSize, Point offset)
        {
            // x: align popup's right edge to header's right edge, minus a small inset
            // y: place just below the header
            var point = new Point(targetSize.Width - popupSize.Width - RightInset, targetSize.Height);

            return new[]
            {
                new CustomPopupPlacement(point, PopupPrimaryAxis.Horizontal)
            };
        }

        // Tools ▸ View Logs...
        private void mnuToolsViewLogs_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
            {
                mw.ShowLogsPanel();
            }
        }

        // Tools -> App Settings...
        private void mnuToolsAppSettings_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
            {
                mw.ShowAppSettingsPanel();
            }
        }

        public void SetToolsNavigationEnabled(bool enabled) => ToolsMenu.IsEnabled = enabled;

        private async void mnuToolsPurgeLogs_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                await mw.PurgeLogsAsync();
        }
    }
}
