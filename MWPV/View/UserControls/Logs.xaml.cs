// File: MWPV/View/UserControls/Logs.xaml.cs
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MWPV.Services;          // LogCatalogService
using MWPV.View.Windows;      // LogDetailsWindow, LogEntry

namespace MWPV.View.UserControls
{
    public partial class Logs : UserControl
    {
        // Raised when the "Close" button is clicked (host/parent can subscribe)
        public event EventHandler? CloseRequested;

        // ---------------- Paging ----------------
        private const int PageSize = 25;   // rows per page
        private int _offset = 0;

        // ---------------- Data backing the grid ----------------
        private readonly ObservableCollection<MWPV.Models.Logs> _rows = new();

        // ---------------- Auto-refresh ----------------
        private readonly DispatcherTimer _timer = new();

        public Logs()
        {
            InitializeComponent();

            // Loaded hook wires auto-refresh controls and kicks initial load
            Loaded += Logs_Loaded;

            // Data source hookup + dbl-click to open details
            if (dgLogs != null)
            {
                dgLogs.ItemsSource = _rows;
                dgLogs.MouseDoubleClick += dgLogs_MouseDoubleClick;
            }

            // timer tick (interval set from UI later)
            _timer.Tick += Timer_Tick;
        }

        // =========================================================
        // Lifecycle
        // =========================================================
        private async void Logs_Loaded(object? sender, RoutedEventArgs e)
        {
            // Initial load = first page
            await LoadFirstPageAsync();

            // Set timer interval from UI and wire check/uncheck
            UpdateTimerIntervalFromUi();

            if (chkAuto != null)
            {
                chkAuto.Checked += (_, __) => _timer.Start();
                chkAuto.Unchecked += (_, __) => _timer.Stop();
            }

            if (cmbSeconds != null)
            {
                cmbSeconds.SelectionChanged += (_, __) => UpdateTimerIntervalFromUi();
            }
        }

        // =========================================================
        // Toolbar handlers
        // =========================================================
        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadFirstPageAsync();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private async void Timer_Tick(object? sender, EventArgs e)
        {
            // Always reload the newest page to avoid jumping around
            await LoadFirstPageAsync(silent: true);
        }

        private void UpdateTimerIntervalFromUi()
        {
            try
            {
                if (cmbSeconds?.SelectedItem is ComboBoxItem item &&
                    int.TryParse(item.Content?.ToString(), out int secs) &&
                    secs > 0)
                {
                    _timer.Interval = TimeSpan.FromSeconds(secs);
                    if (chkAuto?.IsChecked == true) _timer.Start();
                }
            }
            catch
            {
                // ignore minor parsing issues and keep previous interval
            }
        }

        // =========================================================
        // Paging API (hook up to Next/Prev buttons if you add them)
        // =========================================================
        public async Task LoadFirstPageAsync(bool silent = false)
        {
            _offset = 0;
            await LoadPageAsync(_offset, PageSize, silent);
        }

        public async Task LoadNextPageAsync()
        {
            _offset += PageSize;
            await LoadPageAsync(_offset, PageSize);
        }

        public async Task LoadPrevPageAsync()
        {
            _offset = Math.Max(0, _offset - PageSize);
            await LoadPageAsync(_offset, PageSize);
        }

        // =========================================================
        // Core loader
        // =========================================================
        private async Task LoadPageAsync(int offset, int limit, bool silent = false)
        {
            try
            {
                // hop off UI thread
                var list = await Task.Run(() => LogCatalogService.SelectPage(offset, limit));

                _rows.Clear();
                foreach (var r in list)
                    _rows.Add(r);

                if (dgLogs != null && dgLogs.Items.Count > 0)
                    dgLogs.ScrollIntoView(dgLogs.Items[0]);
            }
            catch (Exception ex)
            {
                if (!silent)
                    MessageBox.Show($"Failed to load logs:\n{ex.Message}", "Error",
                                    MessageBoxButton.OK, MessageBoxImage.Error);
                Debug.WriteLine($"[LOGS][LoadPage][FAIL] {ex}");
            }
        }

        // =========================================================
        // Row interactions (double-click + View button)
        // =========================================================
        private void dgLogs_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (dgLogs?.SelectedItem is MWPV.Models.Logs row)
            {
                _ = OpenDetailsAsync(row.Id);
            }
        }

        private async void ViewLog_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is MWPV.Models.Logs row)
            {
                await OpenDetailsAsync(row.Id);
            }
        }

        // =========================================================
        // Details: fetch full record, map to LogEntry, show window
        // =========================================================
        private async Task OpenDetailsAsync(long id)
        {
            try
            {
                // Includes Payload, PayloadFmt, PayloadSize
                var rec = await Task.Run(() => LogCatalogService.SelectById(id));
                if (rec is null)
                    throw new InvalidOperationException($"No log found for id={id}");

                // Map into the dialog's LogEntry (your window expects this)
                var entry = new LogEntry
                {
                    Id = rec.Id.ToString(CultureInfo.InvariantCulture),
                    CreatedUtc = ParseIsoUtc(rec.CreatedUtc),
                    Level = rec.Level ?? string.Empty,
                    Source = rec.Source ?? string.Empty,
                    EventCode = rec.EventCode ?? string.Empty,
                    Payload = rec.Payload ?? string.Empty,        // service already decoded bytes -> string
                    PayloadSize = rec.PayloadSize,
                    PayloadFmt = string.IsNullOrWhiteSpace(rec.PayloadFmt) ? "none" : rec.PayloadFmt!.Trim()
                };

                // Launch your dialog
                var owner = Window.GetWindow(this);
                LogDetailsWindow.ShowSingle(owner!, entry);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to open log details.\n\n{ex}", "Logs",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                Debug.WriteLine($"[LOGS][Details][FAIL] {ex}");
            }
        }

        // =========================================================
        // Helpers
        // =========================================================
        /// <summary>
        /// Parse ISO 8601 string to UTC DateTime (tolerant to Z/offsets).
        /// Falls back to UtcNow on failure.
        /// </summary>
        private static DateTime ParseIsoUtc(string? iso)
        {
            if (!string.IsNullOrWhiteSpace(iso))
            {
                if (DateTimeOffset.TryParse(
                        iso, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out var dto))
                    return dto.UtcDateTime;

                if (DateTime.TryParse(
                        iso, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out var dt))
                    return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            }

            return DateTime.UtcNow;
        }
    }
}
