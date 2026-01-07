// File: View/UserControls/CategoryItemGrid.xaml.cs
//
// FULL REWRITE (restore missing API + keep current event signature)
//
// Fixes your 3 remaining errors by restoring:
//   - Clear()
//   - Refresh(int categoryKey, string? categoryName = null)
//
// Keeps:
//   - event EventHandler<int>? EditRequested  (your “changed” version signature)
//
// Notes:
// - XAML binding: ItemsSource="{Binding BoundCategoryItems, RelativeSource={RelativeSource AncestorType=UserControl}}"
//   works with this property (no DP needed).
// - Refresh() calls CategoryItemService.LoadCategoryItems(categoryKey) and repopulates BoundCategoryItems.

using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MWPV.Models;
using MWPV.Services;

namespace MWPV.View.UserControls
{
    public partial class CategoryItemGrid : UserControl
    {
        // Host subscribes to open the editor overlay (ItemId only).
        public event EventHandler<int>? EditRequested;

        // Bound to the ItemsControl in XAML.
        public ObservableCollection<CategoryItemGriud> BoundCategoryItems { get; } = new();

        private int _currentCategoryKey;
        private string _currentCategoryName = string.Empty;

        public int CurrentCategoryKey => _currentCategoryKey;
        public string CurrentCategoryName => _currentCategoryName;

        public CategoryItemGrid()
        {
            InitializeComponent();

            // Defensive: if anyone ever binds without RelativeSource, this still works.
            // (Your current XAML uses RelativeSource, so this isn't required, but harmless.)
            DataContext = this;

#if DEBUG
            Debug.WriteLine("[ITEMS_GRID][CTOR] Initialized. DataContext=self.");
#endif
        }

        // ------------------------------------------------------------
        // Public API used by Panel (RESTORED)
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
        // Click handler (wired in XAML: Click="ItemPill_Click")
        // ------------------------------------------------------------

        private void ItemPill_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not Button btn)
                    return;

                string label = btn.Content?.ToString() ?? string.Empty;
                string tag = btn.Tag?.ToString() ?? string.Empty;

                // Extra defensive: ignore blank pills even if style fails to collapse.
                if (string.IsNullOrWhiteSpace(label))
                {
#if DEBUG
                    Debug.WriteLine("[ITEMS_GRID][CLICK] Ignored (blank label).");
#endif
                    return;
                }

                if (!TryParseItemId(tag, out int itemId) || itemId <= 0)
                {
#if DEBUG
                    Debug.WriteLine($"[ITEMS_GRID][CLICK] IGNORE label='{label}' tag='{tag}' (invalid ItemId)");
#endif
                    return;
                }

#if DEBUG
                Debug.WriteLine($"[ITEMS_GRID][CLICK] EDIT label='{label}' ItemId={itemId} catKey={_currentCategoryKey} catName='{_currentCategoryName}'");
#endif
                EditRequested?.Invoke(this, itemId);
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine("[ITEMS_GRID][CLICK][ERR] " + ex);
#endif
            }
        }

        // ------------------------------------------------------------
        // Parsing helper
        // ------------------------------------------------------------

        private static bool TryParseItemId(string raw, out int itemId)
        {
            itemId = 0;

            if (string.IsNullOrWhiteSpace(raw))
                return false;

            raw = raw.Trim();

            // Accept int-like tags. Some call sites may pass long as string, so parse long first.
            if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
                return false;

            if (l <= 0 || l > int.MaxValue)
                return false;

            itemId = (int)l;
            return true;
        }
    }
}
