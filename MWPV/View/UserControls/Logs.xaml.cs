// File: MWPV/View/UserControls/Logs.xaml.cs
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MWPV.Services;

namespace MWPV.View.UserControls
{
    public partial class Logs : UserControl
    {
        public event EventHandler? CloseRequested;

        private const int PageSize = 15;
        private int _offset = 0;

        private readonly ObservableCollection<MWPV.Models.Logs> _rows = new();
        private bool _loadedOnce;

        public Logs()
        {
            InitializeComponent();

            dgLogs.ItemsSource = _rows;
            dgLogs.MouseDoubleClick += dgLogs_MouseDoubleClick;

            Loaded += Logs_Loaded;
            IsVisibleChanged += Logs_IsVisibleChanged;
        }

        // ===========================
        // Lifecycle
        // ===========================
        private async void Logs_Loaded(object? sender, RoutedEventArgs e)
        {
            if (!_loadedOnce)
            {
                _loadedOnce = true;
                await LoadFirstPageAsync();
            }
        }

        private async void Logs_IsVisibleChanged(object? sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible && _loadedOnce)
                await LoadFirstPageAsync(silent: true);
        }

        // ===========================
        // Toolbar
        // ===========================
        private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadFirstPageAsync();
        private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

        // ===========================
        // Paging
        // ===========================
        public async Task LoadFirstPageAsync(bool silent = false)
        {
            _offset = 0;
            await LoadPageAsync(_offset, PageSize, silent);
            UpdateStatus();
        }

        public async Task LoadNextPageAsync()
        {
            _offset += PageSize;
            await LoadPageAsync(_offset, PageSize);
            UpdateStatus();
        }

        public async Task LoadPrevPageAsync()
        {
            _offset = Math.Max(0, _offset - PageSize);
            await LoadPageAsync(_offset, PageSize);
            UpdateStatus();
        }

        private async Task LoadPageAsync(int offset, int limit, bool silent = false)
        {
            try
            {
                var list = await Task.Run(() => LogCatalogService.SelectPage(offset, limit));

                _rows.Clear();
                foreach (var r in list) _rows.Add(r);

                if (dgLogs.Items.Count > 0)
                    dgLogs.ScrollIntoView(dgLogs.Items[0]);

                // Auto-clear details if no row
                if (_rows.Count == 0)
                    DetailsPanel.Clear();
            }
            catch (Exception ex)
            {
                if (!silent)
                    MessageBox.Show($"Failed to load logs:\n{ex.Message}", "Error",
                                    MessageBoxButton.OK, MessageBoxImage.Error);
                Debug.WriteLine($"[LOGS][LoadPage][FAIL] {ex}");
            }
        }

        private async void _btnNext_Click(object sender, RoutedEventArgs e) => await LoadNextPageAsync();
        private async void _btnPrev_Click(object sender, RoutedEventArgs e) => await LoadPrevPageAsync();

        private void UpdateStatus()
        {
            int page = (_offset / PageSize) + 1;
            txtStatus.Text = $"Page {page} • Showing {PageSize} rows";
        }

        // ===========================
        // Row Interaction
        // ===========================
        private async void dgLogs_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (dgLogs.SelectedItem is MWPV.Models.Logs row)
                await ShowDetailsAsync(row);
        }

        private async void ViewLog_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is MWPV.Models.Logs row)
                await ShowDetailsAsync(row);
        }

        private async void dgLogs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgLogs.SelectedItem is MWPV.Models.Logs row)
                await ShowDetailsAsync(row, silent: true);
        }

        // ===========================
        // Details
        // ===========================
        private async Task ShowDetailsAsync(MWPV.Models.Logs row, bool silent = false)
        {
            try
            {
                await DetailsPanel.LoadFromAsync(row);
            }
            catch (Exception ex)
            {
                if (!silent)
                {
                    MessageBox.Show($"Unable to load log details.\n\n{ex}", "Logs",
                                    MessageBoxButton.OK, MessageBoxImage.Error);
                }
                Debug.WriteLine($"[LOGS][Details][FAIL] {ex}");
            }
        }
    }
}
