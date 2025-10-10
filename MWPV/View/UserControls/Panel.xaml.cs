using System;
using System.Windows;
using System.Windows.Controls;

namespace MWPV.View.UserControls
{
    public partial class Panel : UserControl
    {
        private AddCategoryInline? _addCategoryInline;
        private bool _isHandlingInlineEvent;

        private int _selectedCategoryKey;
        private string _selectedCategoryName = string.Empty;

        public Panel()
        {
            InitializeComponent();

            // Visual lifecycle
            Loaded += Panel_Loaded;
            Unloaded += Panel_Unloaded;

            // Default left-pane state
            ShowCategoryGrid();

            // Prepare inline “Add Category” user control
            InitializeAddCategoryInline();
        }

        /* ======================= Lifecycle ======================= */

        private void Panel_Loaded(object? sender, RoutedEventArgs e)
        {
            WireCategoryGridEvents();
            WireOverlayEvents();

            // Refresh categories on first show
            SafeRefreshCategories();
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

            // Subscribe to the routed event (SelectedCategoryChanged)
            CategoryGrid.SelectedCategoryChanged -= CategoryGrid_SelectedCategoryChanged;
            CategoryGrid.SelectedCategoryChanged += CategoryGrid_SelectedCategoryChanged;

            // Header + button initial state
            if (txtCategoryItemsTitle != null)
                txtCategoryItemsTitle.Text = "Category Items";

            if (btnAddCategoryItem != null)
                btnAddCategoryItem.Visibility = Visibility.Collapsed;
        }

        private void UnwireCategoryGridEvents()
        {
            if (CategoryGrid == null) return;
            CategoryGrid.SelectedCategoryChanged -= CategoryGrid_SelectedCategoryChanged;
        }

        // RoutedEvent handler — extract payload via CategoryGrid.GetSelectedCategory(e)
        private void CategoryGrid_SelectedCategoryChanged(object sender, RoutedEventArgs e)
        {
            var sel = CategoryGrid.GetSelectedCategory(e);
            _selectedCategoryKey = sel.Key;
            _selectedCategoryName = sel.Name ?? string.Empty;

            // CHANGE: show the button on ANY category click (no Key check)
            if (btnAddCategoryItem != null)
                btnAddCategoryItem.Visibility = Visibility.Visible;

            // Update header with selected category name
            if (txtCategoryItemsTitle != null)
                txtCategoryItemsTitle.Text = string.IsNullOrWhiteSpace(_selectedCategoryName)
                    ? "Category Items"
                    : $"Category Items — {_selectedCategoryName}";

            // TODO (later): load right-side CategoryItem grid for _selectedCategoryKey
            // await ItemsGrid.LoadForCategoryAsync(_selectedCategoryKey, _selectedCategoryName);
        }

        public void RefreshCategoryGrid()
        {
            SafeRefreshCategories();
        }

        private void SafeRefreshCategories()
        {
            try { CategoryGrid?.Refresh(); } catch { /* no-op */ }
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

        // XAML references this handler – keep it
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

            if (txtCategoryItemsTitle != null)
                txtCategoryItemsTitle.Text = "Category Items";
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

            if (txtCategoryItemsTitle != null)
                txtCategoryItemsTitle.Text = "Category Items";
        }

        private void AddCategoryInline_Submitted(object? sender, CategorySubmittedEventArgs e)
        {
            if (_isHandlingInlineEvent) return;
            _isHandlingInlineEvent = true;
            try
            {
                ShowCategoryGrid();
                SafeRefreshCategories();        // repopulate left grid
                _addCategoryInline?.ResetForm();
            }
            finally { _isHandlingInlineEvent = false; }
        }

        private void AddCategoryInline_Canceled(object? sender, EventArgs e)
        {
            if (_isHandlingInlineEvent) return;
            _isHandlingInlineEvent = true;
            try
            {
                ShowCategoryGrid();
                SafeRefreshCategories();        // keep wiring fresh even after cancel
                _addCategoryInline?.ResetForm();
            }
            finally { _isHandlingInlineEvent = false; }
        }

        /* ======================= Logs Overlay ======================= */

        private void WireOverlayEvents()
        {
            if (LogsOverlay == null) return;

            LogsOverlay.CloseRequested -= LogsOverlay_CloseRequested;
            LogsOverlay.CloseRequested += LogsOverlay_CloseRequested;
        }

        private void UnwireOverlayEvents()
        {
            if (LogsOverlay == null) return;
            LogsOverlay.CloseRequested -= LogsOverlay_CloseRequested;
        }

        // MainWindow calls this – keep the public signature
        public void ShowLogs()
        {
            if (OverlayHost != null)
                OverlayHost.Visibility = Visibility.Visible;

            try { LogsOverlay?.Focus(); } catch { /* ignore */ }
        }

        private void LogsOverlay_CloseRequested(object? sender, EventArgs e)
        {
            if (OverlayHost != null)
                OverlayHost.Visibility = Visibility.Collapsed;
        }
    }
}
