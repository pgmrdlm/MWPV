using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MWPV.Services;
using LogRow = MWPV.Models.Logs;

namespace MWPV.View.UserControls
{
    public partial class Logs : UserControl
    {
        public event EventHandler? CloseRequested;

        private readonly ObservableCollection<LogRow> _rows = new();

        public Logs()
        {
            InitializeComponent();

            dgLogs.ItemsSource = _rows;
            btnClose.Click += (_, __) => CloseRequested?.Invoke(this, EventArgs.Empty);

            Loaded += (_, __) => LoadRecent();
        }

        private void LoadRecent()
        {
            _rows.Clear();
            var list = LogCatalogService.SelectRecent(200);
            foreach (var r in list ?? Enumerable.Empty<LogRow>())
                _rows.Add(r);
        }
    }
}
