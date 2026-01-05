// CategoryItemGrid.xaml.cs (FULL REWRITE — minimal changes only)
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
        // Event raised when user clicks an existing item pill.
        // Panel subscribes and decides how to open editor overlay.
        public event EventHandler<CategoryItemEditRequestedEventArgs>? EditRequested;

        // Keeping the model type name as-is to match your existing service contract.
        public ObservableCollection<CategoryItemGriud> BoundCategoryItems { get; } = new();

        private int _currentCategoryKey = 0;
        private string _currentCategoryName = string.Empty;

        public CategoryItemGrid()
        {
            InitializeComponent();
#if DEBUG
            Debug.WriteLine("[ITEMS_GRID][INIT] CategoryItemGrid initialized (content-only).");
#endif
        }

        /// <summary>Clear bound rows.</summary>
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

        /// <summary>Populate items for a category (3-up rows; blanks auto-collapse via style).</summary>
        public void Refresh(int categoryKey, string? categoryName = null)
        {
#if DEBUG
            Debug.WriteLine($"[ITEMS_GRID][REFRESH][ENTER] key={categoryKey}, name='{categoryName ?? _currentCategoryName}'");
#endif
            _currentCategoryKey = categoryKey;
            if (!string.IsNullOrWhiteSpace(categoryName))
                _currentCategoryName = categoryName!;

            try { BoundCategoryItems.Clear(); } catch { /* ignore */ }

            if (categoryKey <= 0)
            {
#if DEBUG
                Debug.WriteLine("[ITEMS_GRID][REFRESH] Skipped because key <= 0.");
                Debug.WriteLine("[ITEMS_GRID][REFRESH][EXIT]");
#endif
                return;
            }

            try
            {
#if DEBUG
                Debug.WriteLine("[ITEMS_GRID][REFRESH] Calling CategoryItemService.LoadCategoryItems(...)");
#endif
                var rows = CategoryItemService.LoadCategoryItems(categoryKey);
                if (rows != null)
                {
                    foreach (var r in rows)
                        BoundCategoryItems.Add(r);
                }

#if DEBUG
                Debug.WriteLine($"[ITEMS_GRID][REFRESH] Loaded rows={BoundCategoryItems.Count} for key={categoryKey}, name='{_currentCategoryName}'");
                int i = 0;
                foreach (var r in BoundCategoryItems)
                    Debug.WriteLine($"[ITEMS_GRID][ROW {i++}] '{r.strCategoryItem1}' | '{r.strCategoryItem2}' | '{r.strCategoryItem3}'");
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

        private void ItemPill_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not Button btn)
                    return;

                // Buttons with blank content are Collapsed, but keep a defensive guard.
                var content = btn.Content as string;
                if (string.IsNullOrWhiteSpace(content))
                    return;

                // Tag is bound to strCategoryItemKeyN (string). Parse to int.
                var raw = btn.Tag?.ToString() ?? string.Empty;
                if (!int.TryParse(raw, out int categoryItemId) || categoryItemId <= 0)
                {
#if DEBUG
                    Debug.WriteLine($"[ITEMS_GRID][CLICK] Invalid item id in Tag='{raw}'.");
#endif
                    return;
                }

#if DEBUG
                Debug.WriteLine($"[ITEMS_GRID][CLICK] Edit requested: itemId={categoryItemId}, catKey={_currentCategoryKey}, catName='{_currentCategoryName}', label='{content}'");
#endif

                EditRequested?.Invoke(this, new CategoryItemEditRequestedEventArgs(
                    categoryItemId,
                    _currentCategoryKey,
                    _currentCategoryName
                ));
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
