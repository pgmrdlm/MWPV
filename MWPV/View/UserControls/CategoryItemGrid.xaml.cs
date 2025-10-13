using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Controls;
using MWPV.Models;
using MWPV.Services;

namespace MWPV.View.UserControls
{
    public partial class CategoryItemGrid : UserControl
    {
        // Keep model name as-is to match existing services.
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

        /// <summary>
        /// Clears the bound rows.
        /// </summary>
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

        /// <summary>
        /// Load items for the given category. No header or button here—those live in Panel.
        /// </summary>
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
    }
}
