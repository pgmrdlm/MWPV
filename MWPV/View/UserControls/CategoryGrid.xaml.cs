using MWPV.Models;
using MWPV.Services;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace MWPV.View.UserControls
{
    /// <summary>
    /// 3-column category grid with fixed pill width. Button text shows:
    /// - full text if length <= 17
    /// - otherwise first 14 chars + "..."
    /// Tooltip shows full text + extra tooltip line.
    /// Raises SelectedCategoryChanged with (Key, Name).
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

        public void Refresh()
        {
            BoundCategories.Clear();
            foreach (var c in CategoryService.LoadCategories())
                BoundCategories.Add(c);
        }

        /* ================== UI ================== */

        private void OnCategoryButtonClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;

            // Content is a TextBlock
            var name = (btn.Content as TextBlock)?.Text ?? string.Empty;

            // Tag holds the key
            int key = 0;
            if (btn.Tag is int k) key = k;
            else if (btn.Tag is long l) key = (int)l;
            else if (btn.Tag is string s && int.TryParse(s, out var parsed)) key = parsed;

            var payload = new CategorySelected(key, name);
            var args = new CategorySelectedRoutedEventArgs(SelectedCategoryChangedEvent, this, payload);
            RaiseEvent(args);
        }
    }

    /// <summary>
    /// If text length <= max (parameter, default 17), return as-is.
    /// Otherwise return first 14 chars + "...".
    /// You can override both with ConverterParameter "max,keep".
    /// e.g. "17,14" or just "17".
    /// </summary>
    public sealed class CategoryCropConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var s = value as string ?? string.Empty;

            int max = 17;
            int keep = 14;

            if (parameter is string p && !string.IsNullOrWhiteSpace(p))
            {
                var parts = p.Split(',');
                if (parts.Length >= 1 && int.TryParse(parts[0], out var m)) max = m;
                if (parts.Length >= 2 && int.TryParse(parts[1], out var k)) keep = k;
            }

            if (s.Length <= max) return s;
            if (keep < 0) keep = 0;
            if (keep > s.Length) keep = Math.Min(keep, s.Length);

            return (keep > 0 ? s.Substring(0, keep) : string.Empty) + "...";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
