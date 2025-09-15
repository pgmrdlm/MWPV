// File: MWPV/View/UserControls/Logs.xaml.cs
using MWPV.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Utilities.Helpers;

namespace MWPV.View.UserControls
{
    public partial class Logs : UserControl
    {
        public event EventHandler? CloseRequested;

        private const int PageSize = 15;
        private int _offset = 0;

        private readonly ObservableCollection<MWPV.Models.Logs> _rows = new();
        private readonly List<LogTypeItem> _types = new();
        private string _selectedTypeCode = "ALL";
        private bool _loadedOnce;

        // --- window title handling ------------------------------------------
        private string? _origTitle;
        private const string TitleSuffix = " – Log Display";

        public Logs()
        {
            InitializeComponent();

            ListPanel.ItemsSource = _rows;

            Loaded += Logs_Loaded;
            Unloaded += Logs_Unloaded;
            IsVisibleChanged += Logs_IsVisibleChanged;
        }

        // --- lifecycle -------------------------------------------------------

        private async void Logs_Loaded(object? sender, RoutedEventArgs e)
        {
            // Ensure the title is decorated as soon as we show
            ApplyTitleSuffix(isActive: true);

            if (_loadedOnce) return;
            _loadedOnce = true;

            await LoadTypesAsync();
            await LoadFirstPageAsync();
        }

        private async void Logs_IsVisibleChanged(object? sender, DependencyPropertyChangedEventArgs e)
        {
            // Keep title in sync with visibility of this view
            ApplyTitleSuffix(isActive: IsVisible);

            if (IsVisible && _loadedOnce)
                await LoadFirstPageAsync(silent: true);
        }

        private void Logs_Unloaded(object? sender, RoutedEventArgs e)
        {
            // When the control is removed/swapped out, restore the title
            ApplyTitleSuffix(isActive: false);
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            // If you trigger closing from inside the control, also restore
            ApplyTitleSuffix(isActive: false);
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        // --- title helpers ---------------------------------------------------

        private void ApplyTitleSuffix(bool isActive)
        {
            var win = Window.GetWindow(this);
            if (win == null) return;

            if (_origTitle is null)
                _origTitle = win.Title;

            if (isActive)
            {
                if (!win.Title.EndsWith(TitleSuffix, StringComparison.OrdinalIgnoreCase))
                    win.Title = $"{_origTitle}{TitleSuffix}";
            }
            else
            {
                if (_origTitle != null && win.Title != _origTitle)
                    win.Title = _origTitle;
            }
        }

        // --- type filter -----------------------------------------------------

        private async Task LoadTypesAsync()
        {
            try
            {
                _types.Clear();

                // Always include "All"
                _types.Add(new LogTypeItem { Code = "ALL", Description = "All" });

                // Pull the rest from combo details (type=log_filters)
                var dbTypes = await Task.Run(() =>
                    LogCatalogService.GetComboDetailsByType("log_filters")); // returns Code/Description

                foreach (var t in dbTypes.OrderBy(t => t.Seq))
                {
                    // skip duplicates of ALL if present
                    if (!string.Equals(t.Code, "ALL", StringComparison.OrdinalIgnoreCase))
                        _types.Add(new LogTypeItem { Code = t.Code, Description = t.Description });
                }

                cmbType.ItemsSource = _types;
                cmbType.SelectedValue = "ALL";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LOGS][Types][FAIL] {ex}");
                MessageBox.Show("Failed to load log types.", "Logs",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                // fall back to just ALL
                if (!_types.Any())
                {
                    _types.Add(new LogTypeItem { Code = "ALL", Description = "All" });
                    cmbType.ItemsSource = _types;
                    cmbType.SelectedValue = "ALL";
                }
            }
        }

        private async void Type_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbType.SelectedValue is string code)
            {
                _selectedTypeCode = code;
                await LoadFirstPageAsync();
            }
        }

        // --- paging ----------------------------------------------------------

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
                var list = await Task.Run(() =>
                    _selectedTypeCode.Equals("ALL", StringComparison.OrdinalIgnoreCase)
                        ? LogCatalogService.SelectPage(offset, limit)
                        : LogCatalogService.SelectPageFiltered(offset, limit, _selectedTypeCode));

                _rows.Clear();
                foreach (var r in list) _rows.Add(r);

                if (_rows.Count == 0)
                    DetailsPanel.Clear();
            }
            catch (Exception ex)
            {
                if (!silent)
                    MessageBox.Show($"Failed to load logs:\n{ex.Message}", "Logs",
                                    MessageBoxButton.OK, MessageBoxImage.Error);
                Debug.WriteLine($"[LOGS][LoadPage][FAIL] {ex}");
            }
        }

        private void UpdateStatus()
        {
            int page = (_offset / PageSize) + 1;
            var typeDesc = _types.FirstOrDefault(t => t.Code == _selectedTypeCode)?.Description ?? _selectedTypeCode;
            ListPanel.StatusText = $"Page {page} • Showing {PageSize} rows • Type: {typeDesc}";
        }

        // --- list panel events ----------------------------------------------

        private async void ListPanel_PrevRequested(object sender, RoutedEventArgs e) =>
            await LoadPrevPageAsync();

        private async void ListPanel_NextRequested(object sender, RoutedEventArgs e) =>
            await LoadNextPageAsync();

        private async void ListPanel_ViewRequested(object sender, RoutedEventArgs e)
        {
            if (ListPanel.SelectedItem is MWPV.Models.Logs row)
                await ShowDetailsAsync(row);
        }

        private async void ListPanel_SelectionChanged(object sender, RoutedEventArgs e)
        {
            if (ListPanel.SelectedItem is MWPV.Models.Logs row)
                await ShowDetailsAsync(row, silent: true);
        }

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
                    // Using your helper; you can swap InfoTitled -> ErrorTitled if you prefer
                    ErrorHandler.InfoTitled(
                        "Logs",
                        $"Unable to load log details.\n\n{ex.Message}\n\n(This has been logged.)",
                        "Logs.Details.Fail"
                    );
                }
                Debug.WriteLine($"[LOGS][Details][FAIL] {ex}");
            }
        }

        // --- helper ----------------------------------------------------------

        private sealed class LogTypeItem
        {
            public string Code { get; set; } = "";
            public string Description { get; set; } = "";
        }
    }
}
