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

            // Ensure we hook/unhook after the visual tree is ready
            Loaded += Panel_Loaded;
            Unloaded += Panel_Unloaded;

            // Default left-pane view
            ShowCategoryGrid();

            // Prepare inline Add Category content
            InitializeAddCategoryInline();
        }

        /* ======================= Lifecycle ======================= */

        private void Panel_Loaded(object? sender, RoutedEventArgs e)
        {
            WireCategoryGridEvents();
            WireOverlayEvents();
        }

        private void Panel_Unloaded(object? sender, RoutedEventArgs e)
        {
            UnwireCategoryGridEvents();
            UnwireOverlayEvents();
        }

        /* =================== Category Grid Area =================== */

        private void WireCategoryGridEvents()
        {
            if (CategoryGrid == null) return;

            // Rewire click handler
            CategoryGrid.CategoryItemClicked -= CategoryGrid_CategoryItemClicked;
            CategoryGrid.CategoryItemClicked += CategoryGrid_CategoryItemClicked;

            // Refresh data + internal wiring (if those methods exist)
            try { CategoryGrid.RefreshCategoryGrid(); } catch { /* no-op */ }
            try { CategoryGrid.EnsureInternalWiring(); } catch { /* no-op */ }

            // Default state: hide "Add Category Item" until a category is clicked
            if (btnAddCategoryItem != null)
                btnAddCategoryItem.Visibility = Visibility.Collapsed;
        }

        private void UnwireCategoryGridEvents()
        {
            if (CategoryGrid == null) return;
            CategoryGrid.CategoryItemClicked -= CategoryGrid_CategoryItemClicked;
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

        /* =================== Add Category Inline =================== */

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

        /* ======================= Logs Overlay ======================= */

        private void WireOverlayEvents()
        {
            if (LogsOverlay == null) return;

            // Rewire the CloseRequested event exposed by Logs.xaml.cs
            LogsOverlay.CloseRequested -= LogsOverlay_CloseRequested;
            LogsOverlay.CloseRequested += LogsOverlay_CloseRequested;
        }

        private void UnwireOverlayEvents()
        {
            if (LogsOverlay == null) return;
            LogsOverlay.CloseRequested -= LogsOverlay_CloseRequested;
        }

        /// <summary>
        /// Shows the logs overlay. The Logs control itself loads/paginates on Loaded.
        /// </summary>
        public void ShowLogs()
        {
            if (OverlayHost != null)
                OverlayHost.Visibility = Visibility.Visible;

            // If you want to ensure Logs refreshes to the first page whenever shown:
            try
            {
                // Expose a public method on Logs (optional) like ResetToFirstPage();
                // LogsOverlay?.ResetToFirstPage();
            }
            catch { /* optional; ignore */ }
        }

        private void LogsOverlay_CloseRequested(object? sender, EventArgs e)
        {
            // Simply hide the overlay host; no need to mutate visual tree
            if (OverlayHost != null)
                OverlayHost.Visibility = Visibility.Collapsed;
        }
    }
}
