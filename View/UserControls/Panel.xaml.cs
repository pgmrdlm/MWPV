using System;
using System.Windows;
using System.Windows.Controls;

namespace MWPV.View.UserControls
{
    /// <summary>
    /// Interaction logic for Panel.xaml
    /// </summary>
    public partial class Panel : UserControl
    {
        private AddCatagoryInline? _addCatagoryInline;

        public Panel()
        {
            InitializeComponent();

            // Wire up CategoryGrid events and load data (REQUIRED TO LOAD DATA)
            CategoryGrid.CategoryItemClicked += CategoryGrid_CategoryItemClicked;
            CategoryGrid.RefreshCategoryGrid();

            // Ensure default view is the Catagory grid
            ShowCategoryGrid();

            // Initialize inline add-cat host
            InitializeAddCatagoryInline();
        }

        /// <summary>
        /// External callers can refresh the catagory grid.
        /// </summary>
        public void RefreshCategoryGrid()
        {
            CategoryGrid.RefreshCategoryGrid();
        }

        /// <summary>
        /// When a category item is clicked inside the grid, reveal the "Add Item" button.
        /// </summary>
        private void CategoryGrid_CategoryItemClicked(object? sender, EventArgs e)
        {
            if (btnAddCategoryItem != null)
                btnAddCategoryItem.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Add Category clicked:
        /// Collapse the Catagory grid and show the inline AddCatagoryInline control.
        /// </summary>
        private void btnAddCategory_Click(object sender, RoutedEventArgs e)
        {
            ShowAddCatagory();
        }

        // --- View toggles (keep control local to Panel) ---

        private void ShowAddCatagory()
        {
            if (AddCatagoryHost != null)
                AddCatagoryHost.Visibility = Visibility.Visible;

            if (CategoryGrid != null)
                CategoryGrid.Visibility = Visibility.Collapsed;
        }

        private void ShowCategoryGrid()
        {
            if (AddCatagoryHost != null)
                AddCatagoryHost.Visibility = Visibility.Collapsed;

            if (CategoryGrid != null)
                CategoryGrid.Visibility = Visibility.Visible;
        }

        // --- Host content management ---

        private void InitializeAddCatagoryInline()
        {
            if (AddCatagoryContent == null)
                return;

            // Clean up old instance if reinitializing
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
            // DB insert already happened inside AddCatagoryInline
            ShowCategoryGrid();
            RefreshCategoryGrid();

            // reset inline form for next time
            _addCatagoryInline?.ResetForm();
        }

        private void AddCatagoryInline_Canceled(object? sender, EventArgs e)
        {
            ShowCategoryGrid();

            // reset inline form so it's clean next time
            _addCatagoryInline?.ResetForm();
        }
    }
}
