// CategoryItemGrid.xaml.cs
// FULL REWRITE — DEBUG-FIRST, MINIMAL BEHAVIOR CHANGES
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using MWPV.Models;
using MWPV.Services;

namespace MWPV.View.UserControls
{
    public partial class CategoryItemGrid : UserControl
    {
        // Panel subscribes to this and decides how to open the editor overlay.
        public event EventHandler<CategoryItemEditRequestedEventArgs>? EditRequested;

        // Matches existing service contract/model naming.
        public ObservableCollection<CategoryItemGriud> BoundCategoryItems { get; } = new();

        private int _currentCategoryKey;
        private string _currentCategoryName = string.Empty;

        public CategoryItemGrid()
        {
            InitializeComponent();

            // Ensure the XAML binding:
            // ItemsSource="{Binding BoundCategoryItems, RelativeSource={RelativeSource AncestorType=UserControl}}"
            // works consistently even if host code forgets to set DataContext.
            DataContext = this;

            Loaded += CategoryItemGrid_Loaded;
            Unloaded += CategoryItemGrid_Unloaded;

#if DEBUG
            Debug.WriteLine("[ITEMS_GRID][CTOR] Initialized. DataContext=self.");
#endif
        }

        private void CategoryItemGrid_Loaded(object sender, RoutedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[ITEMS_GRID][LOADED]");
#endif
            WireClickHandlers();
        }

        private void CategoryItemGrid_Unloaded(object sender, RoutedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[ITEMS_GRID][UNLOADED]");
#endif
            UnwireClickHandlers();
        }

        // ------------------------------------------------------------
        // Public API used by Panel
        // ------------------------------------------------------------

        public void Clear()
        {
#if DEBUG
            Debug.WriteLine("[ITEMS_GRID][CLEAR][ENTER]");
#endif
            _currentCategoryKey = 0;
            _currentCategoryName = string.Empty;

            try { BoundCategoryItems.Clear(); } catch { /* ignore */ }

#if DEBUG
            Debug.WriteLine("[ITEMS_GRID][CLEAR][EXIT]");
#endif
        }

        public void Refresh(int categoryKey, string? categoryName = null)
        {
#if DEBUG
            Debug.WriteLine($"[ITEMS_GRID][REFRESH][ENTER] catKey={categoryKey}, catName='{categoryName ?? _currentCategoryName}'");
#endif
            _currentCategoryKey = categoryKey;
            if (!string.IsNullOrWhiteSpace(categoryName))
                _currentCategoryName = categoryName!;

            try { BoundCategoryItems.Clear(); } catch { /* ignore */ }

            if (categoryKey <= 0)
            {
#if DEBUG
                Debug.WriteLine("[ITEMS_GRID][REFRESH] Skipped (catKey <= 0).");
                Debug.WriteLine("[ITEMS_GRID][REFRESH][EXIT]");
#endif
                return;
            }

            try
            {
#if DEBUG
                Debug.WriteLine("[ITEMS_GRID][REFRESH] Calling CategoryItemService.LoadCategoryItems(catKey)...");
#endif
                var rows = CategoryItemService.LoadCategoryItems(categoryKey);
                if (rows != null)
                {
                    foreach (var r in rows)
                        BoundCategoryItems.Add(r);
                }

#if DEBUG
                Debug.WriteLine($"[ITEMS_GRID][REFRESH] Loaded rows={BoundCategoryItems.Count} for catKey={categoryKey}, name='{_currentCategoryName}'");

                int i = 0;
                foreach (var r in BoundCategoryItems)
                {
                    Debug.WriteLine(
                        $"[ITEMS_GRID][ROW {i++}] " +
                        $"Col1='{r.strCategoryItem1}' Key1='{r.strCategoryItemKey1}' | " +
                        $"Col2='{r.strCategoryItem2}' Key2='{r.strCategoryItemKey2}' | " +
                        $"Col3='{r.strCategoryItem3}' Key3='{r.strCategoryItemKey3}'");
                }
#endif
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine($"[ITEMS_GRID][REFRESH][ERR] {ex}");
#endif
            }

#if DEBUG
            Debug.WriteLine("[ITEMS_GRID][REFRESH][EXIT]");
#endif
        }

        // ------------------------------------------------------------
        // Click handling
        // ------------------------------------------------------------

        // Safety net: ensure we always have a click handler even if XAML drifts.
        private void WireClickHandlers()
        {
            // The XAML already has Click="ItemPill_Click".
            // This is just a defensive hook: if templates/styles ever change and lose the Click,
            // we can still capture clicks by walking the visual tree.
            // Minimal approach: do nothing here unless you remove Click from XAML later.
#if DEBUG
            Debug.WriteLine("[ITEMS_GRID][WIRE] (No-op; XAML Click handler is authoritative.)");
#endif
        }

        private void UnwireClickHandlers()
        {
#if DEBUG
            Debug.WriteLine("[ITEMS_GRID][UNWIRE] (No-op; XAML Click handler is authoritative.)");
#endif
        }

        // This must match the XAML: Click="ItemPill_Click"
        private void ItemPill_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not Button btn)
                    return;

                var label = btn.Content?.ToString() ?? string.Empty;
                var rawTag = btn.Tag?.ToString() ?? string.Empty;

#if DEBUG
                Debug.WriteLine($"[ITEMS_GRID][CLICK] rawTag='{rawTag}', label='{label}', catKey={_currentCategoryKey}, catName='{_currentCategoryName}'");
#endif

                // Ignore clicks on blank/hidden pills (extra defensive)
                if (string.IsNullOrWhiteSpace(label))
                {
#if DEBUG
                    Debug.WriteLine("[ITEMS_GRID][CLICK] Ignored (blank label).");
#endif
                    return;
                }

                if (!int.TryParse(rawTag, out int categoryItemId) || categoryItemId <= 0)
                {
#if DEBUG
                    Debug.WriteLine($"[ITEMS_GRID][CLICK] Invalid ItemId in Tag='{rawTag}'.");
#endif
                    return;
                }

#if DEBUG
                Debug.WriteLine($"[ITEMS_GRID][CLICK] EditRequested: itemId={categoryItemId}");
#endif

                EditRequested?.Invoke(
                    this,
                    new CategoryItemEditRequestedEventArgs(
                        categoryItemId,
                        _currentCategoryKey,
                        _currentCategoryName
                    )
                );
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine("[ITEMS_GRID][CLICK][ERR] " + ex);
#endif
            }
        }
    }

    public sealed class CategoryItemEditRequestedEventArgs : EventArgs
    {
        public int CategoryItemId { get; }
        public int CategoryKey { get; }
        public string CategoryName { get; }

        public CategoryItemEditRequestedEventArgs(int categoryItemId, int categoryKey, string categoryName)
        {
            CategoryItemId = categoryItemId;
            CategoryKey = categoryKey;
            CategoryName = categoryName ?? string.Empty;
        }
    }
}
