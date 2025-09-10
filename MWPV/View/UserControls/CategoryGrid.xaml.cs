using MWPV.Models;
using MWPV.Services;
using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace MWPV.View.UserControls
{
    /// <summary>
    /// Interaction logic for CategoryGrid.xaml
    /// </summary>
    public partial class CategoryGrid : UserControl
    {
        public event EventHandler? CategoryItemClicked;

        // ObservableCollection to hold the categories
        public ObservableCollection<Categories> BoundCatagories { get; set; } = new ObservableCollection<Categories>();

        public CategoryGrid()
        {
            InitializeComponent();
            this.DataContext = this;

            // Also wire when the visual tree is ready (e.g., to access named elements)
            this.Loaded += CategoryGrid_Loaded;
        }

        private void CategoryGrid_Loaded(object sender, RoutedEventArgs e)
        {
            EnsureInternalWiring();
        }

        /// <summary>
        /// Ensure internal event wiring (idempotent). Safe to call anytime.
        /// Wires SelectionChanged on the data grid if present.
        /// </summary>
        public void EnsureInternalWiring()
        {
            // If your XAML has a DataGrid named "CategoryDataGrid", wire its SelectionChanged
            var grid = this.FindName("CategoryDataGrid") as DataGrid;
            if (grid != null)
            {
                grid.SelectionChanged -= CategoryDataGrid_SelectionChanged;
                grid.SelectionChanged += CategoryDataGrid_SelectionChanged;
            }
        }

        public void RefreshCategoryGrid()
        {
            BoundCatagories.Clear();
            var catagories = CategoryService.LoadCategories();
            foreach (var cat in catagories)
                BoundCatagories.Add(cat);
        }

        // --- Button click handlers inside the grid/list ---

        private void Button1_Click(object sender, RoutedEventArgs e)
        {
            RaiseCategorySelected();
        }

        private void Button2_Click(object sender, RoutedEventArgs e)
        {
            RaiseCategorySelected();
        }

        private void Button3_Click(object sender, RoutedEventArgs e)
        {
            RaiseCategorySelected();
        }

        // --- Selection change handler (covers row clicks, keyboard nav, etc.) ---

        private void CategoryDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Only fire if a real category is selected
            if ((sender as DataGrid)?.SelectedItem is Categories)
            {
                RaiseCategorySelected();
            }
        }

        private void RaiseCategorySelected()
        {
            CategoryItemClicked?.Invoke(this, EventArgs.Empty);
        }
    }
}
