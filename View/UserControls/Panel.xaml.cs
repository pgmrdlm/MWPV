using System;
using System.Windows;
using System.Windows.Controls;

namespace MWPV.View.UserControls
{
    public partial class Panel : UserControl
    {
        private AddCatagoryInline? _addCatagoryInline;
        private bool _isHandlingInlineEvent;

        public Panel()
        {
            InitializeComponent();
            this.Loaded += Panel_Loaded;

            // Default view
            ShowCategoryGrid();

            InitializeAddCatagoryInline();
        }

        private void Panel_Loaded(object? sender, RoutedEventArgs e)
        {
            WireCategoryGridEvents();
        }

        // Idempotent: safe to call repeatedly
        private void WireCategoryGridEvents()
        {
            if (CategoryGrid == null) return;

            CategoryGrid.CategoryItemClicked -= CategoryGrid_CategoryItemClicked;
            CategoryGrid.CategoryItemClicked += CategoryGrid_CategoryItemClicked;

            // Ensure data and internal wiring are fresh inside the grid
            CategoryGrid.RefreshCategoryGrid();      // repopulate
            CategoryGrid.EnsureInternalWiring();     // NEW: asks grid to wire SelectionChanged safely

            // Add Item hidden until a category is clicked
            if (btnAddCategoryItem != null)
                btnAddCategoryItem.Visibility = Visibility.Collapsed;
        }

        public void RefreshCategoryGrid()
        {
            CategoryGrid?.RefreshCategoryGrid();
            CategoryGrid?.EnsureInternalWiring();
        }

        private void CategoryGrid_CategoryItemClicked(object? sender, EventArgs e)
        {
            if (btnAddCategoryItem != null)
                btnAddCategoryItem.Visibility = Visibility.Visible;
        }

        private void btnAddCategory_Click(object sender, RoutedEventArgs e)
        {
            ShowAddCatagory();
        }

        private void ShowAddCatagory()
        {
            if (AddCatagoryHost != null)
                AddCatagoryHost.Visibility = Visibility.Visible;

            if (btnAddCategory != null)
                btnAddCategory.Visibility = Visibility.Collapsed;

            if (btnAddCategoryItem != null)
                btnAddCategoryItem.Visibility = Visibility.Collapsed;

            if (CategoryGrid != null)
                CategoryGrid.Visibility = Visibility.Collapsed;
        }

        private void ShowCategoryGrid()
        {
            if (AddCatagoryHost != null)
                AddCatagoryHost.Visibility = Visibility.Collapsed;

            if (CategoryGrid != null)
                CategoryGrid.Visibility = Visibility.Visible;

            if (btnAddCategory != null)
                btnAddCategory.Visibility = Visibility.Visible;

            if (btnAddCategoryItem != null)
                btnAddCategoryItem.Visibility = Visibility.Collapsed;
        }

        private void InitializeAddCatagoryInline()
        {
            if (AddCatagoryContent == null) return;

            if (_addCatagoryInline != null)
            {
                _addCatagoryInline.Submitted -= AddCatagoryInline_Submitted;
                _addCatagoryInline.Canceled -= AddCatagoryInline_Canceled;
            }

            _addCatagoryInline = new AddCatagoryInline();
            _addCatagoryInline.Submitted += AddCatagoryInline_Submitted;
            _addCatagoryInline.Canceled += AddCatagoryInline_Canceled;

            AddCatagoryContent.Content = _addCatagoryInline;
        }

        private void AddCatagoryInline_Submitted(object? sender, CatagorySubmittedEventArgs e)
        {
            if (_isHandlingInlineEvent) return;
            _isHandlingInlineEvent = true;
            try
            {
                ShowCategoryGrid();
                RefreshCategoryGrid();      // repopulates
                // NOTE: Add Item remains hidden until a category is clicked
                _addCatagoryInline?.ResetForm();
            }
            finally
            {
                _isHandlingInlineEvent = false;
            }
        }

        private void AddCatagoryInline_Canceled(object? sender, EventArgs e)
        {
            if (_isHandlingInlineEvent) return;
            _isHandlingInlineEvent = true;
            try
            {
                ShowCategoryGrid();
                RefreshCategoryGrid();      // keep wiring fresh even after cancel
                _addCatagoryInline?.ResetForm();
            }
            finally
            {
                _isHandlingInlineEvent = false;
            }
        }
    }
}
