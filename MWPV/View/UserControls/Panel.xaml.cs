// File: View/UserControls/Panel.xaml.cs
//
// FULL REWRITE (crash fix included)
//
// Crash cause:
// - ADD clears SEDS CurrentEntityId/Kind
// - Debug code (or any code) calling SEDS.GetInt32(...) on a cleared key throws KeyNotFoundException
//
// Fix:
// - Use TryGetInt32 for debug reads (and any non-required reads).
// - Keep ADD behavior as "clear keys" (your design), but do NOT "Get" cleared keys.

using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

        // SEDS context keys (reserved + standardized in Security.Utility)
        private static readonly string SedsKey_CurrentEntityId = SecureEncryptedDataStore.ContextKeys.CurrentEntityId;
        private static readonly string SedsKey_CurrentEntityKind = SecureEncryptedDataStore.ContextKeys.CurrentEntityKind;

        // Keep the kind string centralized
        private const string EntityKind_CategoryItem = "CategoryItem";

        public Panel()
        {
            InitializeComponent();

            Loaded += Panel_Loaded;
            Unloaded += Panel_Unloaded;

            ShowCategoryGrid();
            InitializeAddCategoryInline();

            SetNavigationLocked(false);
        }

        /* ======================= Lifecycle ======================= */

        private void Panel_Loaded(object? sender, RoutedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[PANEL][LOADED]");
#endif
            WireCategoryGridEvents();
            WireCategoryItemGridEvents();
            WireOverlayEvents();

            SafeRefreshCategories();
        }

        private void Panel_Unloaded(object? sender, RoutedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[PANEL][UNLOADED]");
#endif
            UnwireCategoryGridEvents();
            UnwireCategoryItemGridEvents();
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

            // Inline add-category host stays visible but inactive when locked.
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
#if DEBUG
                Debug.WriteLine($"[PANEL][RIGHT][REFRESH][ERR] {ex}");
#endif
            }
        }

        /* =================== Category Item Grid (pills) =================== */

        private void WireCategoryItemGridEvents()
        {
            if (CategoryItemGrid == null) return;

            CategoryItemGrid.EditRequested -= CategoryItemGrid_EditRequested;
            CategoryItemGrid.EditRequested += CategoryItemGrid_EditRequested;

#if DEBUG
            Debug.WriteLine("[PANEL][RIGHT] CategoryItemGrid events wired.");
#endif
        }

        private void UnwireCategoryItemGridEvents()
        {
            if (CategoryItemGrid == null) return;

            CategoryItemGrid.EditRequested -= CategoryItemGrid_EditRequested;

#if DEBUG
            Debug.WriteLine("[PANEL][RIGHT] CategoryItemGrid events unwired.");
#endif
        }

        private void CategoryItemGrid_EditRequested(object? sender, int categoryItemId)
        {
            if (_isNavigationLocked)
            {
#if DEBUG
                Debug.WriteLine("[PANEL][ITEM-EDIT] Ignored (navigation locked).");
#endif
                return;
            }

            if (categoryItemId <= 0)
            {
#if DEBUG
                Debug.WriteLine($"[PANEL][ITEM-EDIT] Ignored (invalid itemId={categoryItemId}).");
#endif
                return;
            }

            ShowEditCategoryItemEditor(categoryItemId);
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
            if (_isNavigationLocked) return;

            ShowAddCategoryItemEditor(); // ADD: clears SEDS keys
        }

        public void ShowEditCategoryItemEditor(int categoryItemId)
        {
            if (_isNavigationLocked) return;

            if (_selectedCategoryKey <= 0)
            {
#if DEBUG
                Debug.WriteLine("[PANEL][ITEM-EDIT] Edit requested but no selected category. Ignoring.");
#endif
                return;
            }

            if (categoryItemId <= 0)
            {
#if DEBUG
                Debug.WriteLine("[PANEL][ITEM-EDIT] Edit requested with invalid categoryItemId. Ignoring.");
#endif
                return;
            }

            // EDIT/VIEW: set PK into SEDS BEFORE creating editor
            SetActiveEntityForEdit(categoryItemId);

            CreateAndShowEditorOverlay(_selectedCategoryKey, _selectedCategoryName);
        }

        private void ShowAddCategoryItemEditor()
        {
            if (_selectedCategoryKey <= 0)
            {
#if DEBUG
                Debug.WriteLine("[PANEL][ITEM-ADD] Add requested with no selected category. Ignoring.");
#endif
                return;
            }

            // ADD: clear context BEFORE creating editor
            ClearActiveEntityForAdd();

            CreateAndShowEditorOverlay(_selectedCategoryKey, _selectedCategoryName);
        }

        private void CreateAndShowEditorOverlay(int categoryKey, string categoryName)
        {
            if (_categoryItemEdit != null)
            {
                _categoryItemEdit.Submitted -= CategoryItemEdit_Submitted;
                _categoryItemEdit.Canceled -= CategoryItemEdit_Canceled;
            }

            _categoryItemEdit = new CategoryItemEditorTabs();
            _categoryItemEdit.Submitted += CategoryItemEdit_Submitted;
            _categoryItemEdit.Canceled += CategoryItemEdit_Canceled;

            // Tabs decide ADD vs EDIT by reading SEDS keys.
            _categoryItemEdit.ConfigureForOpen(categoryKey, categoryName);

            AddEditItemOverlayHost.Content = _categoryItemEdit;
            AddEditItemOverlayHost.Visibility = Visibility.Visible;

            SetNavigationLocked(true);

#if DEBUG
            Debug.WriteLine(
                $"[PANEL][ITEM-OVERLAY] Shown. catKey={categoryKey}, catName='{categoryName}', " +
                $"CurrentEntityKind='{TryGetActiveEntityKindDebug()}', CurrentEntityId={TryGetActiveEntityIdDebug()}");
#endif
        }

        private void HideAddEditCategoryItem()
        {
            try { _categoryItemEdit?.WipeAllForHostClose(); } catch { }

            AddEditItemOverlayHost.Visibility = Visibility.Collapsed;
            AddEditItemOverlayHost.Content = null;
            _categoryItemEdit = null;

            // Clear context so a future open never inherits an old Edit id.
            ClearActiveEntityForAdd();

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

        /* =================== SEDS context helpers =================== */

        private void ClearActiveEntityForAdd()
        {
            try
            {
                // Your design: key missing means ADD.
                SecureEncryptedDataStore.Clear(SedsKey_CurrentEntityId);
                SecureEncryptedDataStore.Clear(SedsKey_CurrentEntityKind);

#if DEBUG
                Debug.WriteLine("[PANEL][CTX] Cleared SEDS CurrentEntityId/Kind (ADD).");
#endif
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine($"[PANEL][CTX][WARN] Failed to clear SEDS context: {ex}");
#endif
            }
        }

        private void SetActiveEntityForEdit(int categoryItemId)
        {
            try
            {
                // Write KIND first, then ID. Tabs read kind first.
                SecureEncryptedDataStore.SetString(SedsKey_CurrentEntityKind, EntityKind_CategoryItem);
                SecureEncryptedDataStore.SetInt32(SedsKey_CurrentEntityId, categoryItemId);

#if DEBUG
                Debug.WriteLine($"[PANEL][CTX] Set SEDS Kind={EntityKind_CategoryItem}, CurrentEntityId={categoryItemId} (EDIT/VIEW).");
#endif
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine($"[PANEL][CTX][WARN] Failed to set SEDS context: {ex}");
#endif
            }
        }

#if DEBUG
        private int TryGetActiveEntityIdDebug()
        {
            // CRASH FIX: never call GetInt32 for a key that may be cleared.
            return SecureEncryptedDataStore.TryGetInt32(SedsKey_CurrentEntityId, out int id) ? id : 0;
        }

        private string TryGetActiveEntityKindDebug()
        {
            if (!SecureEncryptedDataStore.TryGetBytes(SedsKey_CurrentEntityKind, out var bytes) || bytes.Length == 0)
                return string.Empty;

            try { return System.Text.Encoding.UTF8.GetString(bytes); }
            finally { Array.Clear(bytes, 0, bytes.Length); }
        }
#endif

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
#if DEBUG
                Debug.WriteLine($"[PANEL][REFRESH][ERR] {ex}");
#endif
            }
        }
    }
}
