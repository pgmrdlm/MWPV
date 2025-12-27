using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Security.Utility.Storage; // SecureEncryptedDataStore (SEDS)

namespace MWPV.View.UserControls
{
    public partial class Panel : UserControl
    {
        private AddCategoryInline? _addCategoryInline;
        private CategoryItemEditorTabs? _categoryItemEdit;
        private bool _isHandlingInlineEvent;

        private int _selectedCategoryKey;
        private string _selectedCategoryName = string.Empty;

        // When true: left-side navigation is visible but inactive.
        private bool _isNavigationLocked;

        // SEDS key (editor reads this; Panel owns clearing for ADD)
        private const string SedsKey_ActiveEntityId = "MWPV.Context.ActiveEntityId";

        public Panel()
        {
            InitializeComponent();

            Loaded += Panel_Loaded;
            Unloaded += Panel_Unloaded;

            ShowCategoryGrid();
            InitializeAddCategoryInline();

            // Default: navigation unlocked.
            SetNavigationLocked(false);
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

        /* =================== HOST CLOSE BRIDGE (Big X / window close) =================== */

        /// <summary>
        /// MainWindow calls this BEFORE it detaches Content to ensure the active editor
        /// (and child panels like BankCards) gets a host-close wipe ordering.
        /// </summary>
        public void PrepareForHostClose()
        {
            try
            {
                // If the overlay is up, tell the editor to do its host-close wipe path
                if (AddEditItemOverlayHost?.Visibility == Visibility.Visible)
                {
                    if (AddEditItemOverlayHost.Content is CategoryItemEditorTabs tabs)
                    {
#if DEBUG
                        Debug.WriteLine("[PANEL][HOST-CLOSE] Forwarding wipe to CategoryItemEditorTabs.");
#endif
                        tabs.WipeAllForHostClose();
                    }
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine("[PANEL][HOST-CLOSE][ERR] " + ex);
#endif
            }
        }

        /* =================== Navigation Lock =================== */

        // Rule: when the right-side editor overlay is shown, lock left-side navigation.
        private void SetNavigationLocked(bool locked)
        {
            _isNavigationLocked = locked;

            // Left side
            if (btnAddCategory != null) btnAddCategory.IsEnabled = !locked;
            if (CategoryGrid != null) CategoryGrid.IsEnabled = !locked;

            // If inline add-category host is visible, keep it visible but inactive when locked.
            if (AddCategoryHost != null) AddCategoryHost.IsEnabled = !locked;

            // Right-side Add Item button should not be clickable while editor overlay is up.
            if (btnAddCategoryItem != null) btnAddCategoryItem.IsEnabled = !locked;

#if DEBUG
            Debug.WriteLine($"[PANEL][NAV-LOCK] locked={locked}");
#endif
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
            // Hard guard: if nav is locked, ignore selection changes.
            if (_isNavigationLocked)
            {
#if DEBUG
                Debug.WriteLine("[PANEL][LEFT→RIGHT] Selection ignored (navigation locked).");
#endif
                return;
            }

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
            // If navigation is locked, ignore.
            if (_isNavigationLocked) return;

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
            finally
            {
                _isHandlingInlineEvent = false;
            }
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
            finally
            {
                _isHandlingInlineEvent = false;
            }
        }

        /* =================== Add/Edit Category Item =================== */

        private void btnAddCategoryItem_Click(object sender, RoutedEventArgs e)
        {
            // If navigation is locked, ignore.
            if (_isNavigationLocked) return;

            ShowAddEditCategoryItem();
        }

        private void ClearActiveEntityIdForAdd()
        {
            try
            {
                SecureEncryptedDataStore.Clear(SedsKey_ActiveEntityId);
#if DEBUG
                Debug.WriteLine("[PANEL][ITEM-ADD] Cleared SEDS ActiveEntityId for ADD.");
#endif
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine($"[PANEL][ITEM-ADD][WARN] Failed to clear SEDS ActiveEntityId: {ex}");
#endif
            }
        }

        private void ShowAddEditCategoryItem()
        {
            if (_selectedCategoryKey == 0)
            {
#if DEBUG
                Debug.WriteLine("[PANEL][ITEM-EDIT] Add requested with no selected category. Ignoring.");
#endif
                return;
            }

            // ADD MODE RULE: caller clears SEDS BEFORE editor is created/shown.
            ClearActiveEntityIdForAdd();

            if (_categoryItemEdit != null)
            {
                _categoryItemEdit.Submitted -= CategoryItemEdit_Submitted;
                _categoryItemEdit.Canceled -= CategoryItemEdit_Canceled;
            }

            _categoryItemEdit = new CategoryItemEditorTabs();
            _categoryItemEdit.Submitted += CategoryItemEdit_Submitted;
            _categoryItemEdit.Canceled += CategoryItemEdit_Canceled;

            // Editor receives category context only. Mode is determined by SEDS (read-only).
            _categoryItemEdit.ConfigureForAdd(_selectedCategoryKey, _selectedCategoryName);

            AddEditItemOverlayHost.Content = _categoryItemEdit;
            AddEditItemOverlayHost.Visibility = Visibility.Visible;

            // Lock navigation while the editor overlay is up.
            SetNavigationLocked(true);
        }

        private void HideAddEditCategoryItem()
        {
            // Optional-but-safe: ensure wipe ordering even if something hides us without Save/Cancel.
            try
            {
                _categoryItemEdit?.WipeAllForHostClose();
            }
            catch { /* no-op */ }

            AddEditItemOverlayHost.Visibility = Visibility.Collapsed;
            AddEditItemOverlayHost.Content = null;
            _categoryItemEdit = null;

            // Unlock navigation now that the editor overlay is gone.
            SetNavigationLocked(false);
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
            try
            {
                CategoryGrid?.Refresh();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PANEL][REFRESH][ERR] {ex}");
            }
        }
    }
}
