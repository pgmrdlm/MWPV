using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace MWPV.View.UserControls
{
    public partial class Panel : UserControl
    {
        private AddCategoryInline? _addCategoryInline;
        private CategoryitemEditNew? _categoryItemEdit;
        private bool _isHandlingInlineEvent;

        private int _selectedCategoryKey;
        private string _selectedCategoryName = string.Empty;

        public Panel()
        {
            InitializeComponent();

            Loaded += Panel_Loaded;
            Unloaded += Panel_Unloaded;

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

            txtCategoryItemsTitle.Text = "Category Items";
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

        private void CategoryGrid_SelectedCategoryChanged(object sender, RoutedEventArgs e)
        {
            var sel = CategoryGrid.GetSelectedCategory(e);
            _selectedCategoryKey = sel.Key;
            _selectedCategoryName = sel.Name ?? string.Empty;

#if DEBUG
            Debug.WriteLine($"[PANEL][LEFT→RIGHT] Category selected: key={_selectedCategoryKey}, name='{_selectedCategoryName}'");
#endif

            btnAddCategoryItem.Visibility = Visibility.Visible;
            txtCategoryItemsTitle.Text = string.IsNullOrWhiteSpace(_selectedCategoryName)
                ? "Category Items"
                : $"Category Items — {_selectedCategoryName}";

            try
            {
                CategoryItemGrid?.Refresh(_selectedCategoryKey, _selectedCategoryName);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PANEL][RIGHT][REFRESH][ERR] {ex}");
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
        }

        private void btnAddCategory_Click(object sender, RoutedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[PANEL][ADD-CATEGORY-INLINE] ShowAddCategory() requested.");
#endif
            ShowAddCategory();
        }

        private void ShowAddCategory()
        {
            AddCategoryHost.Visibility = Visibility.Visible;
            CategoryGrid.Visibility = Visibility.Collapsed;
            btnAddCategory.Visibility = Visibility.Collapsed;
            btnAddCategoryItem.Visibility = Visibility.Collapsed;

            txtCategoryItemsTitle.Text = "Category Items";
            try { CategoryItemGrid?.Clear(); } catch { }
        }

        private void ShowCategoryGrid()
        {
            AddCategoryHost.Visibility = Visibility.Collapsed;
            CategoryGrid.Visibility = Visibility.Visible;
            btnAddCategory.Visibility = Visibility.Visible;
            btnAddCategoryItem.Visibility = Visibility.Collapsed;
            txtCategoryItemsTitle.Text = "Category Items";
        }

        private void AddCategoryInline_Submitted(object? sender, CategorySubmittedEventArgs e)
        {
            if (_isHandlingInlineEvent) return;
            _isHandlingInlineEvent = true;
            try
            {
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
                ShowCategoryGrid();
                SafeRefreshCategories();
                _addCategoryInline?.ResetForm();
            }
            finally { _isHandlingInlineEvent = false; }
        }

        /* =================== Add/Edit Category Item =================== */

        private void btnAddCategoryItem_Click(object sender, RoutedEventArgs e)
        {
            ShowAddEditCategoryItem();
        }

        private void ShowAddEditCategoryItem()
        {
            if (_categoryItemEdit != null)
            {
                _categoryItemEdit.Submitted -= CategoryItemEdit_Submitted;
                _categoryItemEdit.Canceled -= CategoryItemEdit_Canceled;
            }

            _categoryItemEdit = new CategoryitemEditNew();
            _categoryItemEdit.Submitted += CategoryItemEdit_Submitted;
            _categoryItemEdit.Canceled += CategoryItemEdit_Canceled;

            _categoryItemEdit.ConfigureForAdd(_selectedCategoryKey, _selectedCategoryName);

            AddEditItemOverlayHost.Content = _categoryItemEdit;
            AddEditItemOverlayHost.Visibility = Visibility.Visible;
        }

        private void HideAddEditCategoryItem()
        {
            AddEditItemOverlayHost.Visibility = Visibility.Collapsed;
            AddEditItemOverlayHost.Content = null;
            _categoryItemEdit = null;
        }

        private void CategoryItemEdit_Submitted(object? sender, EventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[PANEL][ITEM-EDIT][SUBMIT] Hiding overlay, refreshing grid.");
#endif
            HideAddEditCategoryItem();
            CategoryItemGrid?.Refresh(_selectedCategoryKey, _selectedCategoryName);
        }

        private void CategoryItemEdit_Canceled(object? sender, EventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[PANEL][ITEM-EDIT][CANCEL] Hiding overlay.");
#endif
            HideAddEditCategoryItem();
        }

        /* ======================= Logs Overlay ======================= */

        private void WireOverlayEvents()
        {
            if (LogsOverlay == null) return;
            LogsOverlay.CloseRequested -= LogsOverlay_CloseRequested;
            LogsOverlay.CloseRequested += LogsOverlay_CloseRequested;
        }

        private void UnwireOverlayEvents()
        {
            if (LogsOverlay == null) return;
            LogsOverlay.CloseRequested -= LogsOverlay_CloseRequested;
        }

        public void ShowLogs()
        {
            OverlayHost.Visibility = Visibility.Visible;
            try { LogsOverlay?.Focus(); } catch { }
        }

        private void LogsOverlay_CloseRequested(object? sender, EventArgs e)
        {
            OverlayHost.Visibility = Visibility.Collapsed;
        }

        /* ======================= Helpers ======================= */

        private void SafeRefreshCategories()
        {
            try { CategoryGrid?.Refresh(); }
            catch (Exception ex) { Debug.WriteLine($"[PANEL][REFRESH][ERR] {ex}"); }
        }
    }
}
