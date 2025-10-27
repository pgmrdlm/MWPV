using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace MWPV.View.UserControls
{
    public partial class Panel : UserControl
    {
        /* ========================= Fields ========================= */

        private AddCategoryInline? _addCategoryInline;
        private bool _isHandlingInlineEvent;

        private int _selectedCategoryKey = 0;
        private string _selectedCategoryName = string.Empty;

        /* ======================= Ctor/Lifecycle ==================== */

        public Panel()
        {
            InitializeComponent();

            Loaded += Panel_Loaded;
            Unloaded += Panel_Unloaded;

            // Initial visual state
            EnsureAddCategoryInlineCreated();
            ShowLeft_CategoryGrid();
            ShowRight_ItemGrid();
        }

        private void Panel_Loaded(object? sender, RoutedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[PANEL][LOADED]");
#endif
            WireCategoryGridEvents();
            WireOverlayEvents();

            // Hard set to always 50/50 (defensive)
            TryForceFiftyFifty();

            // Initial load of categories
            SafeRefreshCategories();
        }

        private void Panel_Unloaded(object? sender, RoutedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[PANEL][UNLOADED]");
#endif
            UnwireCategoryGridEvents();
            UnwireOverlayEvents();
        }

        /* =================== Layout / Split Helpers =================== */

        private void TryForceFiftyFifty()
        {
            try
            {
                if (SplitRoot?.ColumnDefinitions is { Count: 2 })
                {
                    SplitRoot.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                    SplitRoot.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
                }
            }
            catch { /* ignore */ }
        }

        /* ====================== Left (Categories) ====================== */

        private void WireCategoryGridEvents()
        {
            if (CategoryGrid == null) return;

            CategoryGrid.SelectedCategoryChanged -= CategoryGrid_SelectedCategoryChanged;
            CategoryGrid.SelectedCategoryChanged += CategoryGrid_SelectedCategoryChanged;

#if DEBUG
            Debug.WriteLine("[PANEL][LEFT] CategoryGrid events wired.");
#endif
        }

        private void UnwireCategoryGridEvents()
        {
            if (CategoryGrid == null) return;

            CategoryGrid.SelectedCategoryChanged -= CategoryGrid_SelectedCategoryChanged;

#if DEBUG
            Debug.WriteLine("[PANEL][LEFT] CategoryGrid events unwired.");
#endif
        }

        private void CategoryGrid_SelectedCategoryChanged(object sender, RoutedEventArgs e)
        {
            // CategoryGrid must expose GetSelectedCategory(e) → (Key, Name)
            var sel = CategoryGrid.GetSelectedCategory(e);
            _selectedCategoryKey = sel.Key;
            _selectedCategoryName = sel.Name ?? string.Empty;

#if DEBUG
            Debug.WriteLine($"[PANEL][LEFT→RIGHT] Category selected: key={_selectedCategoryKey}, name='{_selectedCategoryName}'");
#endif

            UpdateRightHeaderAndAddButton();

            // Hide any inline "Add Item" editor when switching categories
            ShowRight_ItemGrid();

            // Refresh right grid
            SafeRefreshItems(_selectedCategoryKey, _selectedCategoryName);
        }

        private void SafeRefreshCategories()
        {
#if DEBUG
            Debug.WriteLine("[PANEL][LEFT][REFRESH] SafeRefreshCategories()");
#endif
            try { CategoryGrid?.Refresh(); }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine($"[PANEL][LEFT][REFRESH][ERR] {ex}");
#endif
            }
        }

        /* ----------------- Add Category Inline (Left) ----------------- */

        private void EnsureAddCategoryInlineCreated()
        {
            if (AddCategoryContent == null) return;

            if (_addCategoryInline != null)
            {
                _addCategoryInline.Submitted -= AddCategoryInline_Submitted;
                _addCategoryInline.Canceled -= AddCategoryInline_Canceled;
            }

            _addCategoryInline = new AddCategoryInline();
            _addCategoryInline.Submitted += AddCategoryInline_Submitted;
            _addCategoryInline.Canceled += AddCategoryInline_Canceled;

            AddCategoryContent.Content = _addCategoryInline;

#if DEBUG
            Debug.WriteLine("[PANEL][ADD-CATEGORY-INLINE] Created and injected.");
#endif
        }

        private void btnAddCategory_Click(object sender, RoutedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[PANEL][LEFT] Add Category clicked.");
#endif
            ShowLeft_AddCategoryInline();

            // When adding a category, the right side should be neutral (no item add showing)
            ShowRight_ItemGrid();
            ClearRightHeaderAndAddButton();
        }

        private void AddCategoryInline_Submitted(object? sender, CategorySubmittedEventArgs e)
        {
            if (_isHandlingInlineEvent) return;
            _isHandlingInlineEvent = true;
            try
            {
#if DEBUG
                Debug.WriteLine("[PANEL][LEFT] AddCategoryInline Submitted → return to grid + refresh.");
#endif
                _addCategoryInline?.ResetForm();
                ShowLeft_CategoryGrid();
                SafeRefreshCategories();
            }
            finally { _isHandlingInlineEvent = false; }
        }

        private void AddCategoryInline_Canceled(object? sender, EventArgs e)
        {
            if (_isHandlingInlineEvent) return;
            _isHandlingInlineEvent = true;
            try
            {
#if DEBUG
                Debug.WriteLine("[PANEL][LEFT] AddCategoryInline Canceled → return to grid.");
#endif
                _addCategoryInline?.ResetForm();
                ShowLeft_CategoryGrid();
            }
            finally { _isHandlingInlineEvent = false; }
        }

        private void ShowLeft_AddCategoryInline()
        {
            if (AddCategoryHost != null) AddCategoryHost.Visibility = Visibility.Visible;
            if (CategoryGrid != null) CategoryGrid.Visibility = Visibility.Collapsed;
            if (btnAddCategory != null) btnAddCategory.Visibility = Visibility.Collapsed;
        }

        private void ShowLeft_CategoryGrid()
        {
            if (AddCategoryHost != null) AddCategoryHost.Visibility = Visibility.Collapsed;
            if (CategoryGrid != null) CategoryGrid.Visibility = Visibility.Visible;
            if (btnAddCategory != null) btnAddCategory.Visibility = Visibility.Visible;
        }

        /* ===================== Right (Category Items) ===================== */

        private void UpdateRightHeaderAndAddButton()
        {
            if (txtCategoryItemsTitle != null)
            {
                txtCategoryItemsTitle.Text = string.IsNullOrWhiteSpace(_selectedCategoryName)
                    ? "Category Items"
                    : $"Category Items — {_selectedCategoryName}";
            }

            if (btnAddCategoryItem != null)
            {
                btnAddCategoryItem.Visibility =
                    _selectedCategoryKey > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void ClearRightHeaderAndAddButton()
        {
            if (txtCategoryItemsTitle != null) txtCategoryItemsTitle.Text = "Category Items";
            if (btnAddCategoryItem != null) btnAddCategoryItem.Visibility = Visibility.Collapsed;
        }

        private void SafeRefreshItems(int key, string name)
        {
#if DEBUG
            Debug.WriteLine("[PANEL][RIGHT][REFRESH] SafeRefreshItems()");
#endif
            try { CategoryItemGrid?.Refresh(key, name); }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine($"[PANEL][RIGHT][REFRESH][ERR] {ex}");
#endif
            }
        }

        private void SafeClearItems()
        {
            try { CategoryItemGrid?.Clear(); }
            catch { /* ignore */ }
        }

        /* ------------------ Add Item Inline (Right) ------------------ */

        private void btnAddCategoryItem_Click(object sender, RoutedEventArgs e)
        {
            // Only if a category is selected
            if (_selectedCategoryKey <= 0)
            {
#if DEBUG
                Debug.WriteLine("[PANEL][RIGHT] Add Item clicked with no category selected (ignored).");
#endif
                return;
            }

#if DEBUG
            Debug.WriteLine("[PANEL][RIGHT] Add Item clicked → show inline editor.");
#endif
            ShowRight_AddItemInline();
        }

        private void ShowRight_AddItemInline()
        {
            // Hide the grid, show the inline card (host fills the right column)
            if (CategoryItemGrid != null) CategoryItemGrid.Visibility = Visibility.Collapsed;
            if (AddItemHost != null) AddItemHost.Visibility = Visibility.Visible;
        }

        private void ShowRight_ItemGrid()
        {
            // Hide the inline card, show the grid
            if (AddItemHost != null) AddItemHost.Visibility = Visibility.Collapsed;
            if (CategoryItemGrid != null) CategoryItemGrid.Visibility = Visibility.Visible;

            // Defensive: clear any transient UI state in the grid if needed
            SafeClearItems();
        }

        /* ======================= Logs Overlay ======================= */

        private void WireOverlayEvents()
        {
            if (LogsOverlay == null) return;

            LogsOverlay.CloseRequested -= LogsOverlay_CloseRequested;
            LogsOverlay.CloseRequested += LogsOverlay_CloseRequested;

#if DEBUG
            Debug.WriteLine("[PANEL][OVERLAY] Wired.");
#endif
        }

        private void UnwireOverlayEvents()
        {
            if (LogsOverlay == null) return;

            LogsOverlay.CloseRequested -= LogsOverlay_CloseRequested;

#if DEBUG
            Debug.WriteLine("[PANEL][OVERLAY] Unwired.");
#endif
        }

        public void ShowLogs()
        {
            if (OverlayHost != null) OverlayHost.Visibility = Visibility.Visible;
            try { LogsOverlay?.Focus(); } catch { /* ignore */ }

#if DEBUG
            Debug.WriteLine("[PANEL][OVERLAY] ShowLogs()");
#endif
        }

        private void LogsOverlay_CloseRequested(object? sender, EventArgs e)
        {
            if (OverlayHost != null) OverlayHost.Visibility = Visibility.Collapsed;

#if DEBUG
            Debug.WriteLine("[PANEL][OVERLAY] Close requested.");
#endif
        }

        /* ====================== Public API (optional) ====================== */

        public void RefreshCategoryGrid()
        {
#if DEBUG
            Debug.WriteLine("[PANEL][LEFT][REFRESH] RefreshCategoryGrid() (public).");
#endif
            SafeRefreshCategories();
        }

        public void ResetRightPanel()
        {
            _selectedCategoryKey = 0;
            _selectedCategoryName = string.Empty;

            ShowRight_ItemGrid();
            ClearRightHeaderAndAddButton();
        }
    }

    // IMPORTANT:
    // We intentionally DO NOT declare CategorySubmittedEventArgs here to avoid
    // CS0229 ambiguity. Use the existing project-wide definition.
}
