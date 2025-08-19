using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace MWPV.View.UserControls
{
    public partial class Panel : UserControl
    {
        public Panel()
        {
            InitializeComponent();
            Loaded += Panel_Loaded;
            Unloaded += Panel_Unloaded;

            if (btnAddCategoryItem != null)
                btnAddCategoryItem.Click += btnAddCategoryItem_Click;
        }

        private void Panel_Loaded(object sender, RoutedEventArgs e)
        {
            // subscribe safely (avoid dup)
            if (CategoryGrid != null)
            {
                CategoryGrid.CategoryPillClicked -= OnCategoryPillClicked;
                CategoryGrid.CategoryPillClicked += OnCategoryPillClicked;
                CategoryGrid.RefreshCategoryGrid();
            }
        }

        private void Panel_Unloaded(object? sender, RoutedEventArgs e)
        {
            if (CategoryGrid != null)
                CategoryGrid.CategoryPillClicked -= OnCategoryPillClicked;

            if (btnAddCategoryItem != null)
                btnAddCategoryItem.Click -= btnAddCategoryItem_Click;
        }

        // Left: Add Category
        private void btnAddCategory_Click(object sender, RoutedEventArgs e)
        {
            CategoryGrid?.ShowAddPanel();
        }

        // Fired when a category pill is clicked
        private void OnCategoryPillClicked(object? sender, object row)
        {
            btnAddCategoryItem.Visibility = Visibility.Visible;
            CategoryItemsHost.Content = new TextBlock
            {
                Text = row?.ToString() ?? "Item list placeholder",
                Margin = new Thickness(8)
            };
        }

        // Right: Add Item (stub)
        private void btnAddCategoryItem_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Add Item clicked (stub).");
        }
    }
}
