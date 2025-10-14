using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace MWPV.View.UserControls
{
    public partial class Panel : UserControl
    {
        private AddCategoryInline? _addCategoryInline;
        private bool _isHandlingInlineEvent;

        private int _selectedCategoryKey;
        private string _selectedCategoryName = string.Empty;

        public Panel()
        {
            InitializeComponent();

            // lifecycle
            Loaded += Panel_Loaded;
            Unloaded += Panel_Unloaded;

            // default visual
            ShowCategoryGrid();
            InitializeAddCategoryInline();
        }

        /* ======================= Lifecycle ======================= */

        private void Panel_Loaded(object? sender, RoutedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[PANEL][LOADED]");
#endif
            WireCategoryGridEvents();
            WireOverlayEvents();
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

        /* =================== Category Grid Area =================== */

        private void WireCategoryGridEvents()
        {
            if (CategoryGrid == null) return;

            CategoryGrid.SelectedCategoryChanged -= CategoryGrid_SelectedCategoryChanged;
            CategoryGrid.SelectedCategoryChanged += CategoryGrid_SelectedCategoryChanged;

            // Reset right header and button
            if (txtCategoryItemsTitle != null)
                txtCategoryItemsTitle.Text = "Category Items";

            if (btnAddCategoryItem != null)
                btnAddCategoryItem.Visibility = Visibility.Collapsed;

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

        // RoutedEvent handler — extract payload via CategoryGrid.GetSelectedCategory(e)
        private void CategoryGrid_SelectedCategoryChanged(object sender, RoutedEventArgs e)
        {
            var sel = CategoryGrid.GetSelectedCategory(e);
            _selectedCategoryKey = sel.Key;
            _selectedCategoryName = sel.Name ?? string.Empty;

#if DEBUG
            Debug.WriteLine($"[PANEL][LEFT→RIGHT] Category selected: key={_selectedCategoryKey}, name='{_selectedCategoryName}'");
#endif

            // Show Add Item button
            if (btnAddCategoryItem != null)
                btnAddCategoryItem.Visibility = Visibility.Visible;

            // Update header to include selected name
            if (txtCategoryItemsTitle != null)
            {
                txtCategoryItemsTitle.Text = string.IsNullOrWhiteSpace(_selectedCategoryName)
                    ? "Category Items"
                    : $"Category Items — {_selectedCategoryName}";
            }

            // Refresh right grid
            try
            {
#if DEBUG
                Debug.WriteLine("[PANEL][RIGHT][REFRESH][CALL] CategoryItemGrid.Refresh(...)");
#endif
                CategoryItemGrid?.Refresh(_selectedCategoryKey, _selectedCategoryName);
#if DEBUG
                Debug.WriteLine("[PANEL][RIGHT][REFRESH][RETURN]");
#endif
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine($"[PANEL][RIGHT][REFRESH][ERR] {ex}");
#endif
            }
        }

        public void RefreshCategoryGrid()
        {
#if DEBUG
            Debug.WriteLine("[PANEL][LEFT][REFRESH][PUBLIC] RefreshCategoryGrid() called.");
#endif
            SafeRefreshCategories();
        }

        private void SafeRefreshCategories()
        {
#if DEBUG
            Debug.WriteLine("[PANEL][LEFT][REFRESH][ENTER] SafeRefreshCategories()");
#endif
            try
            {
                CategoryGrid?.Refresh();
#if DEBUG
                Debug.WriteLine("[PANEL][LEFT][REFRESH][EXIT] CategoryGrid.Refresh() returned.");
#endif
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine($"[PANEL][LEFT][REFRESH][ERR] {ex}");
#endif
            }
        }

        /* =================== Add Category Inline =================== */

        private void InitializeAddCategoryInline()
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
            Debug.WriteLine("[PANEL][ADD-CATEGORY-INLINE] Initialized.");
#endif
        }

        // XAML wires this handler
        private void btnAddCategory_Click(object sender, RoutedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[PANEL][ADD-CATEGORY-INLINE] ShowAddCategory() requested.");
#endif
            ShowAddCategory();
        }

        private void ShowAddCategory()
        {
            if (AddCategoryHost != null)
                AddCategoryHost.Visibility = Visibility.Visible;

            if (CategoryGrid != null)
                CategoryGrid.Visibility = Visibility.Collapsed;

            if (btnAddCategory != null)
                btnAddCategory.Visibility = Visibility.Collapsed;

            if (btnAddCategoryItem != null)
                btnAddCategoryItem.Visibility = Visibility.Collapsed;

            if (txtCategoryItemsTitle != null)
                txtCategoryItemsTitle.Text = "Category Items";

            try { CategoryItemGrid?.Clear(); } catch { /* ignore */ }

#if DEBUG
            Debug.WriteLine("[PANEL][ADD-CATEGORY-INLINE] Visible; category grid hidden; right cleared.");
#endif
        }

        private void ShowCategoryGrid()
        {
            if (AddCategoryHost != null)
                AddCategoryHost.Visibility = Visibility.Collapsed;

            if (CategoryGrid != null)
                CategoryGrid.Visibility = Visibility.Visible;

            if (btnAddCategory != null)
                btnAddCategory.Visibility = Visibility.Visible;

            if (btnAddCategoryItem != null)
                btnAddCategoryItem.Visibility = Visibility.Collapsed;

            if (txtCategoryItemsTitle != null)
                txtCategoryItemsTitle.Text = "Category Items";

#if DEBUG
            Debug.WriteLine("[PANEL][ADD-CATEGORY-INLINE] Hidden; category grid visible.");
#endif
        }

        private void AddCategoryInline_Submitted(object? sender, CategorySubmittedEventArgs e)
        {
            if (_isHandlingInlineEvent) return;
            _isHandlingInlineEvent = true;
            try
            {
#if DEBUG
                Debug.WriteLine("[PANEL][ADD-CATEGORY-INLINE][SUBMIT] Returning to grid and refreshing.");
#endif
                ShowCategoryGrid();
                SafeRefreshCategories();
                _addCategoryInline?.ResetForm();
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
                Debug.WriteLine("[PANEL][ADD-CATEGORY-INLINE][CANCEL] Returning to grid and refreshing.");
#endif
                ShowCategoryGrid();
                SafeRefreshCategories();
                _addCategoryInline?.ResetForm();
            }
            finally { _isHandlingInlineEvent = false; }
        }

        /* ======================= Logs Overlay ======================= */

        private void WireOverlayEvents()
        {
            if (LogsOverlay == null) return;
            LogsOverlay.CloseRequested -= LogsOverlay_CloseRequested;
            LogsOverlay.CloseRequested += LogsOverlay_CloseRequested;

#if DEBUG
            Debug.WriteLine("[PANEL][OVERLAY] LogsOverlay events wired.");
#endif
        }

        private void UnwireOverlayEvents()
        {
            if (LogsOverlay == null) return;
            LogsOverlay.CloseRequested -= LogsOverlay_CloseRequested;

#if DEBUG
            Debug.WriteLine("[PANEL][OVERLAY] LogsOverlay events unwired.");
#endif
        }

        // Called by MainWindow
        public void ShowLogs()
        {
            if (OverlayHost != null)
                OverlayHost.Visibility = Visibility.Visible;

            try { LogsOverlay?.Focus(); } catch { /* ignore */ }

#if DEBUG
            Debug.WriteLine("[PANEL][OVERLAY] ShowLogs() visible.");
#endif
        }

        private void LogsOverlay_CloseRequested(object? sender, EventArgs e)
        {
            if (OverlayHost != null)
                OverlayHost.Visibility = Visibility.Collapsed;

#if DEBUG
            Debug.WriteLine("[PANEL][OVERLAY] Close requested; overlay hidden.");
#endif
        }
    }
}
