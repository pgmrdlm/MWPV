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
            Loaded += Panel_Loaded;

            // Default view
            ShowCategoryGrid();

            InitializeAddCategoryInline();
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
            CategoryGrid.EnsureInternalWiring();     // keep selection wiring fresh

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
            ShowAddCategory();
        }

        private void ShowAddCategory()
        {
            if (AddCategoryHost != null)
                AddCategoryHost.Visibility = Visibility.Visible;

            if (btnAddCategory != null)
                btnAddCategory.Visibility = Visibility.Collapsed;

            if (btnAddCategoryItem != null)
                btnAddCategoryItem.Visibility = Visibility.Collapsed;

            if (CategoryGrid != null)
                CategoryGrid.Visibility = Visibility.Collapsed;
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

        private void AddCategoryInline_Submitted(object? sender, CategorySubmittedEventArgs e)
        {
            if (_isHandlingInlineEvent) return;
            _isHandlingInlineEvent = true;
            try
            {
                ShowCategoryGrid();
                RefreshCategoryGrid();      // repopulates
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
    }
}
