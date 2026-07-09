using System;
using System.Windows;
using System.Windows.Controls;

namespace MWPV.View.UserControls
{
    public partial class CategoryCell : UserControl
    {
        public static readonly DependencyProperty CategoryKeyProperty =
            DependencyProperty.Register(
                nameof(CategoryKey),
                typeof(int?),
                typeof(CategoryCell),
                new PropertyMetadata(null, OnCategoryChanged));

        public static readonly DependencyProperty CategoryNameProperty =
            DependencyProperty.Register(
                nameof(CategoryName),
                typeof(string),
                typeof(CategoryCell),
                new PropertyMetadata(string.Empty, OnCategoryChanged));

        public static readonly DependencyProperty CategoryDescriptionProperty =
            DependencyProperty.Register(
                nameof(CategoryDescription),
                typeof(string),
                typeof(CategoryCell),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty IsCategoryActiveProperty =
            DependencyProperty.Register(
                nameof(IsCategoryActive),
                typeof(bool?),
                typeof(CategoryCell),
                new PropertyMetadata(true));

        public int? CategoryKey
        {
            get => (int?)GetValue(CategoryKeyProperty);
            set => SetValue(CategoryKeyProperty, value);
        }

        public string CategoryName
        {
            get => (string)GetValue(CategoryNameProperty);
            set => SetValue(CategoryNameProperty, value);
        }

        public string CategoryDescription
        {
            get => (string)GetValue(CategoryDescriptionProperty);
            set => SetValue(CategoryDescriptionProperty, value);
        }

        public bool? IsCategoryActive
        {
            get => (bool?)GetValue(IsCategoryActiveProperty);
            set => SetValue(IsCategoryActiveProperty, value);
        }

        public event EventHandler<CategoryCellEventArgs>? CategorySelected;
        public event EventHandler<CategoryCellEventArgs>? EditRequested;

        public CategoryCell()
        {
            InitializeComponent();
            UpdateVisibility();
        }

        private static void OnCategoryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CategoryCell cell)
                cell.UpdateVisibility();
        }

        private void UpdateVisibility()
        {
            bool hasCategory = CategoryKey.GetValueOrDefault() > 0 &&
                               !string.IsNullOrWhiteSpace(CategoryName);

            if (CellRoot != null)
                CellRoot.Visibility = hasCategory ? Visibility.Visible : Visibility.Collapsed;
        }

        private void CategoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryBuildArgs(out var args))
                return;

            CategorySelected?.Invoke(this, args);
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryBuildArgs(out var args))
                return;

            EditRequested?.Invoke(this, args);
        }

        private bool TryBuildArgs(out CategoryCellEventArgs args)
        {
            args = default!;

            int key = CategoryKey.GetValueOrDefault();
            if (key <= 0 || string.IsNullOrWhiteSpace(CategoryName))
                return false;

            args = new CategoryCellEventArgs(key, CategoryName, CategoryDescription, IsCategoryActive != false);
            return true;
        }
    }

    public sealed class CategoryCellEventArgs : EventArgs
    {
        public CategoryCellEventArgs(int key, string name, string? description, bool isActive)
        {
            Key = key;
            Name = name ?? string.Empty;
            Description = description ?? string.Empty;
            IsActive = isActive;
        }

        public int Key { get; }
        public string Name { get; }
        public string Description { get; }
        public bool IsActive { get; }
    }
}
