using System;
using System.Windows;
using System.Windows.Controls;

namespace MWPV.View.UserControls
{
    public partial class Panel : UserControl
    {
        private AddCategoryInline? _addCategoryInline;
        private bool _isHandlingInlineEvent;

        public Panel()
        {
            InitializeComponent();

            // Ensure both overlay and grid wiring happen after tree ready
            Loaded += Panel_Loaded;

            // Default left-pane view
            ShowCategoryGrid();

            // Prepare inline Add Category content
            InitializeAddCategoryInline();
        }

        private void Panel_Loaded(object? sender, RoutedEventArgs e)
        {
            WireCategoryGridEvents();
            WireOverlayEvents();
        }

        // ---- Category grid wiring / refresh ----
        private void WireCategoryGridEvents()
        {
            if (CategoryGrid == null) return;

            CategoryGrid.CategoryItemClicked -= CategoryGrid_CategoryItemClicked;
            CategoryGrid.CategoryItemClicked += CategoryGrid_CategoryItemClicked;

            try { CategoryGrid.RefreshCategoryGrid(); } catch { /* no-op */ }
            try { CategoryGrid.EnsureInternalWiring(); } catch { /* no-op */ }

            if (btnAddCategoryItem != null)
                btnAddCategoryItem.Visibility = Visibility.Collapsed;
        }

        public void RefreshCategoryGrid()
        {
            try { CategoryGrid?.RefreshCategoryGrid(); } catch { /* no-op */ }
            try { CategoryGrid?.EnsureInternalWiring(); } catch { /* no-op */ }
        }

        private void CategoryGrid_CategoryItemClicked(object? sender, EventArgs e)
        {
            if (btnAddCategoryItem != null)
                btnAddCategoryItem.Visibility = Visibility.Visible;
        }

        // ---- Inline Add Category flow ----
        private void InitializeAddCategoryInline()
        {
            if (AddCategoryContent == null) return;

            if (_addCategoryInline != null)
            {
                _addCategoryInline.Submitted -= AddCategoryInline_Submitted;
                _addCategoryInline.Canceled -= AddCategoryInline_Canceled;
            }

            _addCategoryInline = new AddCategoryInline();
            _addCategoryInline.Submitted += AddCategoryInline_Submitted;
            _addCategoryInline.Canceled += AddCategoryInline_Canceled;

            AddCategoryContent.Content = _addCategoryInline;
        }

        private void btnAddCategory_Click(object sender, RoutedEventArgs e)
        {
            ShowAddCategory();
        }

        private void ShowAddCategory()
        {
            if (AddCategoryHost != null)
                AddCategoryHost.Visibility = Visibility.Visible;

            if (CategoryGrid != null)
                CategoryGrid.Visibility = Visibility.Collapsed;

            if (btnAddCategory != null)
                btnAddCategory.Visibility = Visibility.Collapsed;

            if (btnAddCategoryItem != null)
                btnAddCategoryItem.Visibility = Visibility.Collapsed;
        }

        private void ShowCategoryGrid()
        {
            if (AddCategoryHost != null)
                AddCategoryHost.Visibility = Visibility.Collapsed;

            if (CategoryGrid != null)
                CategoryGrid.Visibility = Visibility.Visible;

            if (btnAddCategory != null)
                btnAddCategory.Visibility = Visibility.Visible;

            if (btnAddCategoryItem != null)
                btnAddCategoryItem.Visibility = Visibility.Collapsed;
        }

        private void AddCategoryInline_Submitted(object? sender, CategorySubmittedEventArgs e)
        {
            if (_isHandlingInlineEvent) return;
            _isHandlingInlineEvent = true;
            try
            {
                ShowCategoryGrid();
                RefreshCategoryGrid();      // repopulates and rewires
                _addCategoryInline?.ResetForm();
            }
            finally
            {
                _isHandlingInlineEvent = false;
            }
        }

        private void AddCategoryInline_Canceled(object? sender, EventArgs e)
        {
            if (_isHandlingInlineEvent) return;
            _isHandlingInlineEvent = true;
            try
            {
                ShowCategoryGrid();
                RefreshCategoryGrid();      // keep wiring fresh even after cancel
                _addCategoryInline?.ResetForm();
            }
            finally
            {
                _isHandlingInlineEvent = false;
            }
        }

        // ---- Logs overlay ----
        private void WireOverlayEvents()
        {
            if (LogsOverlay == null) return;

            LogsOverlay.CloseRequested -= LogsOverlay_CloseRequested;
            LogsOverlay.CloseRequested += LogsOverlay_CloseRequested;
        }

        public void ShowLogs()
        {
            if (OverlayHost != null)
                OverlayHost.Visibility = Visibility.Visible;
        }

        private void LogsOverlay_CloseRequested(object? sender, EventArgs e)
        {
            if (OverlayHost != null)
                OverlayHost.Visibility = Visibility.Collapsed;
        }
    }
}
