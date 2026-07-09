using System;
using System.Windows;
using System.Windows.Controls;

namespace MWPV.View.UserControls
{
    public partial class AppSettingsPanel : UserControl
    {
        public event EventHandler? SaveRequested;
        public event EventHandler? CancelRequested;

        public AppSettingsPanel()
        {
            InitializeComponent();
        }

        private void btnReset_Click(object sender, RoutedEventArgs e)
        {
            if (txtStatus != null)
                txtStatus.Text = string.Empty;
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            SaveRequested?.Invoke(this, EventArgs.Empty);
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            CancelRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
