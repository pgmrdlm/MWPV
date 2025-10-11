using MWPV.Models;
using MWPV.Services;
using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace MWPV.View.UserControls
{
    /// <summary>
    /// A 3-column category-item button grid that:
    /// - Uses themed pill buttons
    /// - Collapses empty cells
    /// - Raises SelectedCategoryItemChanged with (Key, Name) when a button is clicked
    /// </summary>
    public partial class CategoryItemGrid : UserControl
    {
        /* ================== Routed Event (with payload) ================== */

        public readonly struct CategoryItemSelected
        {
            public int Key { get; }
            public string Name { get; }
            public CategoryItemSelected(int key, string name)
            {
                Key = key;
                Name = name ?? string.Empty;
            }
        }

        public static readonly RoutedEvent SelectedCategoryItemChangedEvent =
            EventManager.RegisterRoutedEvent(
                nameof(SelectedCategoryItemChanged),
                RoutingStrategy.Bubble,
                typeof(RoutedEventHandler),
                typeof(CategoryItemGrid));

        /// <summary>
        /// Fired when any category item button is clicked.
        /// Use GetSelectedItem(e) to read (Key, Name).
        /// </summary>
        public event RoutedEventHandler SelectedCategoryItemChanged
        {
            add => AddHandler(SelectedCategoryItemChangedEvent, value);
            remove => RemoveHandler(SelectedCategoryItemChangedEvent, value);
        }

        public static CategoryItemSelected GetSelectedItem(RoutedEventArgs e)
            => e is CategoryItemSelectedRoutedEventArgs ce ? ce.Payload : default;

        private sealed class CategoryItemSelectedRoutedEventArgs : RoutedEventArgs
        {
            public CategoryItemSelected Payload { get; }
            public CategoryItemSelectedRoutedEventArgs(RoutedEvent routedEvent, object source, CategoryItemSelected payload)
                : base(routedEvent, source) => Payload = payload;
        }

        /* ================== Data ================== */

        public ObservableCollection<CategoryItemGriud> BoundCategoryItems { get; } = new();

        public CategoryItemGrid()
        {
            InitializeComponent();
            DataContext = this;
            Loaded += (_, __) => Refresh(1);  // temporary test category key
        }

        /// <summary>Reloads category items from the database for a given key.</summary>
        public void Refresh(int categoryKey)
        {
            BoundCategoryItems.Clear();
            foreach (var item in CategoryItemService.LoadCategoryItems(categoryKey))
                BoundCategoryItems.Add(item);
        }

        /* ================== UI handlers ================== */

        private void OnCategoryItemButtonClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;

            var name = btn.Content as string ?? string.Empty;
            int key = 0;
            if (btn.Tag is int k) key = k;
            else if (btn.Tag is long l) key = (int)l;
            else if (btn.Tag is string s && int.TryParse(s, out var parsed)) key = parsed;

            var payload = new CategoryItemSelected(key, name);
            var args = new CategoryItemSelectedRoutedEventArgs(SelectedCategoryItemChangedEvent, this, payload);
            RaiseEvent(args);
        }
    }
}
