using MWPV.Models;
using MWPV.Services;
using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace MWPV.View.UserControls
{
    /// <summary>
    /// A 3-column category button grid that:
    /// - Preserves the app's themed button style (pill look)
    /// - Collapses empty cells
    /// - Raises SelectedCategoryChanged with (Key, Name) when a button is clicked
    /// </summary>
    public partial class CategoryGrid : UserControl
    {
        /* ================== Routed Event (with payload) ================== */

        public readonly struct CategorySelected
        {
            public int Key { get; }
            public string Name { get; }
            public CategorySelected(int key, string name)
            {
                Key = key;
                Name = name ?? string.Empty;
            }
        }

        public static readonly RoutedEvent SelectedCategoryChangedEvent =
            EventManager.RegisterRoutedEvent(
                nameof(SelectedCategoryChanged),
                RoutingStrategy.Bubble,
                typeof(RoutedEventHandler),
                typeof(CategoryGrid));

        /// <summary>
        /// Fired when any category button is clicked.
        /// Use GetSelectedCategory(e) to read (Key, Name).
        /// </summary>
        public event RoutedEventHandler SelectedCategoryChanged
        {
            add => AddHandler(SelectedCategoryChangedEvent, value);
            remove => RemoveHandler(SelectedCategoryChangedEvent, value);
        }

        public static CategorySelected GetSelectedCategory(RoutedEventArgs e)
            => e is CategorySelectedRoutedEventArgs ce ? ce.Payload : default;

        private sealed class CategorySelectedRoutedEventArgs : RoutedEventArgs
        {
            public CategorySelected Payload { get; }
            public CategorySelectedRoutedEventArgs(RoutedEvent routedEvent, object source, CategorySelected payload)
                : base(routedEvent, source) => Payload = payload;
        }

        /* ================== Data ================== */

        public ObservableCollection<Categories> BoundCategories { get; } = new();

        public CategoryGrid()
        {
            InitializeComponent();
            DataContext = this;
            Loaded += (_, __) => Refresh();
        }

        /// <summary>Reloads from service.</summary>
        public void Refresh()
        {
            BoundCategories.Clear();
            foreach (var c in CategoryService.LoadCategories())
                BoundCategories.Add(c);
        }

        /* ================== UI handlers ================== */

        private void OnCategoryButtonClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;

            var name = btn.Content as string ?? string.Empty;
            int key = 0;
            if (btn.Tag is int k) key = k;
            else if (btn.Tag is long l) key = (int)l;
            else if (btn.Tag is string s && int.TryParse(s, out var parsed)) key = parsed;

            var payload = new CategorySelected(key, name);
            var args = new CategorySelectedRoutedEventArgs(SelectedCategoryChangedEvent, this, payload);
            RaiseEvent(args);
        }
    }
}
