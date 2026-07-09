// File: View/UserControls/Panel.xaml.cs
//
// FULL REWRITE
//
// Fix made:
// - ForceCancelActiveEditor_BestEffort now calls CategoryItemEditorTabs.ForceCancelFromHost() directly
//   (no reflection, no missing method/property names).
// - Marshals to UI thread safely before touching UI controls.

using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Security.Utility.Storage; // SecureEncryptedDataStore (SEDS)
using MWPV.Services;
using MWPV.View.UserControls.Popup;
using Utilities.Helpers;

namespace MWPV.View.UserControls
{
    public partial class Panel : UserControl
    {
        private AddCategoryInline? _addCategoryInline;
        private CategoryItemEditorTabs? _categoryItemEdit;

        private bool _isHandlingInlineEvent;

        private int _selectedCategoryKey;
        private string _selectedCategoryName = string.Empty;
        private bool _selectedCategoryIsActive;
        private CategoryItemService.CategoryItemGridViewMode _categoryItemViewMode =
            CategoryItemService.CategoryItemGridViewMode.ActiveItems;

        // When true: navigation is visible but inactive.
        private bool _isNavigationLocked;

        // Popup (modal)
        private PopupDialog? _activePopup;
        private TaskCompletionSource<PopupDialog.PopupResult>? _popupTcs;

        // SEDS context keys (reserved + standardized in Security.Utility)
        private static readonly string SedsKey_CurrentEntityId = SecureEncryptedDataStore.ContextKeys.CurrentEntityId;
        private static readonly string SedsKey_CurrentEntityKind = SecureEncryptedDataStore.ContextKeys.CurrentEntityKind;

        private const string EntityKind_CategoryItem = "CategoryItem";

        // -------- Lockdown event bridge to MainWindow --------
        public event EventHandler<NavigationLockChangedEventArgs>? NavigationLockChanged;

        public sealed class NavigationLockChangedEventArgs : EventArgs
        {
            public bool IsLocked { get; }
            public string Message { get; }

            public NavigationLockChangedEventArgs(bool isLocked, string message)
            {
                IsLocked = isLocked;
                Message = message ?? string.Empty;
            }
        }

        public bool IsEditorOverlayActive =>
            AddEditItemOverlayHost != null && AddEditItemOverlayHost.Visibility == Visibility.Visible;

        public bool IsPopupOverlayActive =>
            PopupOverlayHost != null && PopupOverlayHost.Visibility == Visibility.Visible;

        private const string LockdownMessage =
            "Navigation is disabled while the Category Item editor is open. Close the editor (Save / Cancel / Exit) to continue. This insures data security.";

        public Panel()
        {
            InitializeComponent();

            Loaded += Panel_Loaded;
            Unloaded += Panel_Unloaded;

            ShowCategoryGrid();
            InitializeAddCategoryInline();
            ApplyCategoryViewMode(CategoryViewMode.InUse, refresh: false, clearSelection: false);

            SetNavigationLocked(false);
            UpdateLockdownBanner(false);

            // Ensure popup overlay is hidden on init
            try
            {
                if (PopupOverlayHost != null) PopupOverlayHost.Visibility = Visibility.Collapsed;
                if (PopupOverlayContent != null) PopupOverlayContent.Content = null;
            }
            catch { }
        }

        /* ======================= Lifecycle ======================= */

        private void Panel_Loaded(object? sender, RoutedEventArgs e)
        {
            WireCategoryGridEvents();
            WireCategoryItemGridEvents();
            WireOverlayEvents();

            SafeRefreshCategories();
        }

        private void Panel_Unloaded(object? sender, RoutedEventArgs e)
        {
            UnwireCategoryGridEvents();
            UnwireCategoryItemGridEvents();
            UnwireOverlayEvents();

            ForceClosePopupIfAny();
        }

        /* =================== HOST CLOSE BRIDGE (Big X / window close) =================== */

        public void PrepareForHostClose()
        {
            try
            {
                // If popup is active during shutdown, forcibly resolve it as Abort
                if (IsPopupOverlayActive)
                {
                    ForceClosePopupIfAny(PopupDialog.PopupResult.Abort);
                }

                if (IsEditorOverlayActive && AddEditItemOverlayHost.Content is CategoryItemEditorTabs tabs)
                {
                    tabs.WipeAllForHostClose();
                }
            }
            catch (Exception ex)
            {
                _ = FatalErrorPopupHelper.ShowFatalAsync(
                    "MWPV encountered a fatal error while closing and must close.",
                    ex,
                    "Panel shutdown cleanup failed while preparing the host window to close.");
            }
        }

        /// <summary>
        /// Host-close preflight decision bridge.
        /// Returns true when close may continue; false when close should be canceled.
        /// </summary>
        public bool TryHostClosePreflight_BestEffort()
        {
            try
            {
                if (!Dispatcher.CheckAccess())
                    return Dispatcher.Invoke(new Func<bool>(TryHostClosePreflight_BestEffort));

                if (!IsEditorOverlayActive)
                    return true;

                if (AddEditItemOverlayHost?.Content is not CategoryItemEditorTabs tabs)
                    return true;

                return tabs.TryHostClosePreflight();
            }
            catch (Exception ex)
            {
                _ = FatalErrorPopupHelper.ShowFatalAsync(
                    "MWPV encountered a fatal error while closing and must close.",
                    ex,
                    "Panel host-close preflight failed before shutdown could complete.");
                return false;
            }
        }

        /* =================== INACTIVITY BRIDGE (Force Cancel, NOT host-close) =================== */

        /// <summary>
        /// Best-effort "press Cancel" on the active CategoryItem editor.
        /// This is the path inactivity logic should use (it should NOT call PrepareForHostClose()).
        ///
        /// Behavior:
        /// - If popup modal is active: abort it (best-effort)
        /// - If editor overlay is active: trigger existing Cancel flow (CancelRequested) via tabs.ForceCancelFromHost()
        /// </summary>
        public void ForceCancelActiveEditor_BestEffort()
        {
            try
            {
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.BeginInvoke(new Action(ForceCancelActiveEditor_BestEffort));
                    return;
                }

                if (IsPopupOverlayActive)
                {
                    // If a modal popup is blocking, resolve it. Inactivity is a security event.
                    ForceClosePopupIfAny(PopupDialog.PopupResult.Abort);
                }

                if (!IsEditorOverlayActive)
                    return;

                if (AddEditItemOverlayHost?.Content is not CategoryItemEditorTabs tabs)
                    return;

                // This is the correct, real Cancel path:
                // CategoryItemEditorTabs.ForceCancelFromHost -> BasicPanel.ForceCancelFromHost -> CancelRequested -> editor closes.
                tabs.ForceCancelFromHost();
            }
            catch
            {
                // swallow: inactivity must never crash
            }
        }

        /* =================== Navigation Lock + Banner =================== */

        private void SetNavigationLocked(bool locked)
        {
            _isNavigationLocked = locked;

            // Left side
            if (btnAddCategory != null) btnAddCategory.IsEnabled = !locked;
            if (CategoryViewOptionsPanel != null) CategoryViewOptionsPanel.IsEnabled = !locked;
            if (CategoryGrid != null) CategoryGrid.IsEnabled = !locked;

            // Inline add-category host stays visible but inactive when locked.
            if (AddCategoryHost != null) AddCategoryHost.IsEnabled = !locked;

            // Right-side Add Item button should not be clickable while editor overlay is up.
            if (btnAddCategoryItem != null) btnAddCategoryItem.IsEnabled = !locked;
            if (CategoryItemViewOptionsPanel != null) CategoryItemViewOptionsPanel.IsEnabled = !locked;

            UpdateLockdownBanner(locked);
            RaiseNavigationLockChanged(locked);
        }

        private void UpdateLockdownBanner(bool locked)
        {
            try
            {
                if (LockdownBannerText != null)
                    LockdownBannerText.Text = locked ? LockdownMessage : string.Empty;

                if (LockdownBanner != null)
                    LockdownBanner.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
            }
            catch
            {
                // never let UI state updates throw
            }
        }

        private void RaiseNavigationLockChanged(bool locked)
        {
            try
            {
                string msg = locked ? LockdownMessage : string.Empty;
                NavigationLockChanged?.Invoke(this, new NavigationLockChangedEventArgs(locked, msg));
            }
            catch
            {
                // no-op
            }
        }

        /* =================== Category Grid Area =================== */

        private void WireCategoryGridEvents()
        {
            if (CategoryGrid == null) return;

            CategoryGrid.SelectedCategoryChanged -= CategoryGrid_SelectedCategoryChanged;
            CategoryGrid.SelectedCategoryChanged += CategoryGrid_SelectedCategoryChanged;

            CategoryGrid.EditCategoryRequested -= CategoryGrid_EditCategoryRequested;
            CategoryGrid.EditCategoryRequested += CategoryGrid_EditCategoryRequested;

            txtCategoryItemsTitle.Text = "Category Items";
            btnAddCategoryItem.Visibility = Visibility.Collapsed;
        }

        private void UnwireCategoryGridEvents()
        {
            if (CategoryGrid == null) return;

            CategoryGrid.SelectedCategoryChanged -= CategoryGrid_SelectedCategoryChanged;
            CategoryGrid.EditCategoryRequested -= CategoryGrid_EditCategoryRequested;
        }

        private void CategoryGrid_SelectedCategoryChanged(object sender, RoutedEventArgs e)
        {
            if (_isNavigationLocked) return;
            if (IsPopupOverlayActive) return;

            var sel = CategoryGrid.GetSelectedCategory(e);
            _selectedCategoryKey = sel.Key;
            _selectedCategoryName = sel.Name ?? string.Empty;
            _selectedCategoryIsActive = sel.IsActive;

            btnAddCategoryItem.Visibility = _selectedCategoryIsActive
                ? Visibility.Visible
                : Visibility.Collapsed;
            txtCategoryItemsTitle.Text = string.IsNullOrWhiteSpace(_selectedCategoryName)
                ? "Category Items"
                : $"Category Items — {_selectedCategoryName}";

            try { CategoryItemGrid?.Refresh(_selectedCategoryKey, _selectedCategoryName, _categoryItemViewMode); }
            catch { }
        }

        private void CategoryGrid_EditCategoryRequested(object sender, RoutedEventArgs e)
        {
            if (_isNavigationLocked) return;
            if (IsPopupOverlayActive) return;

            var sel = CategoryGrid.GetEditedCategory(e);
            if (sel.Key <= 0) return;

            ShowEditCategory(sel.Key);
        }

        private void CategoryViewRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded && CategoryGrid == null) return;
            if (ReferenceEquals(sender, rbCategoryViewAllActive))
                ApplyCategoryViewMode(CategoryViewMode.AllActive, refresh: true, clearSelection: true);
            else
                ApplyCategoryViewMode(CategoryViewMode.InUse, refresh: true, clearSelection: true);
        }

        private void ApplyCategoryViewMode(CategoryViewMode viewMode, bool refresh, bool clearSelection)
        {
            if (CategoryGrid == null) return;

            if (refresh)
                CategoryGrid.SetCategoryViewMode(viewMode);
            else
                CategoryGrid.CategoryViewMode = viewMode;

            if (clearSelection)
                ClearSelectedCategoryContext();
        }

        private void ClearSelectedCategoryContext()
        {
            _selectedCategoryKey = 0;
            _selectedCategoryName = string.Empty;
            _selectedCategoryIsActive = false;

            if (btnAddCategoryItem != null)
                btnAddCategoryItem.Visibility = Visibility.Collapsed;

            if (txtCategoryItemsTitle != null)
                txtCategoryItemsTitle.Text = "Category Items";

            try { CategoryItemGrid?.Clear(); } catch { }
        }

        private void CategoryItemViewRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (ReferenceEquals(sender, rbCategoryItemViewAllItems))
                _categoryItemViewMode = CategoryItemService.CategoryItemGridViewMode.AllItems;
            else
                _categoryItemViewMode = CategoryItemService.CategoryItemGridViewMode.ActiveItems;

            if (_selectedCategoryKey <= 0)
            {
                try { CategoryItemGrid?.Clear(); } catch { }
                return;
            }

            try { CategoryItemGrid?.Refresh(_selectedCategoryKey, _selectedCategoryName, _categoryItemViewMode); }
            catch { }
        }

        /* =================== Category Item Grid (pills) =================== */

        private void WireCategoryItemGridEvents()
        {
            if (CategoryItemGrid == null) return;

            CategoryItemGrid.EditRequested -= CategoryItemGrid_EditRequested;
            CategoryItemGrid.EditRequested += CategoryItemGrid_EditRequested;
        }

        private void UnwireCategoryItemGridEvents()
        {
            if (CategoryItemGrid == null) return;

            CategoryItemGrid.EditRequested -= CategoryItemGrid_EditRequested;
        }

        private void CategoryItemGrid_EditRequested(object? sender, int categoryItemId)
        {
            if (_isNavigationLocked) return;
            if (IsPopupOverlayActive) return;

            if (categoryItemId <= 0) return;
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
            if (_isNavigationLocked) return;
            if (IsPopupOverlayActive) return;

            ShowAddCategory();
        }

        private void ShowAddCategory()
        {
            _addCategoryInline?.ConfigureForAdd();
            ShowCategoryForm();

            txtCategoryItemsTitle.Text = "Category Items";
            try { CategoryItemGrid?.Clear(); } catch { }
        }

        private void ShowEditCategory(int categoryKey)
        {
            var detail = CategoryService.LoadCategoryByKey(categoryKey);
            if (detail == null)
                return;

            _addCategoryInline?.ConfigureForEdit(detail);
            ShowCategoryForm();
        }

        private void ShowCategoryForm()
        {
            AddCategoryHost.Visibility = Visibility.Visible;
            CategoryGrid.Visibility = Visibility.Collapsed;
            btnAddCategory.Visibility = Visibility.Collapsed;
            btnAddCategoryItem.Visibility = Visibility.Collapsed;
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
            if (IsPopupOverlayActive) return;

            _isHandlingInlineEvent = true;
            try
            {
                ShowCategoryGrid();
                SafeRefreshCategories();
                if (e.Mode == CategoryFormMode.Edit &&
                    e.CategoryKey == _selectedCategoryKey &&
                    (e.IsActive || CategoryGrid.CategoryViewMode == CategoryViewMode.AllActive))
                {
                    RestoreSelectedCategoryContext(e.CategoryKey, e.Name, e.IsActive);
                }
                else if (e.Mode == CategoryFormMode.Edit &&
                         e.CategoryKey == _selectedCategoryKey &&
                         !e.IsActive)
                {
                    ClearSelectedCategoryContext();
                }

                _addCategoryInline?.ConfigureForAdd();
            }
            finally { _isHandlingInlineEvent = false; }
        }

        private void AddCategoryInline_Canceled(object? sender, EventArgs e)
        {
            if (_isHandlingInlineEvent) return;
            if (IsPopupOverlayActive) return;

            _isHandlingInlineEvent = true;
            try
            {
                bool wasEditingSelectedCategory =
                    _addCategoryInline?.Mode == CategoryFormMode.Edit &&
                    _addCategoryInline.EditingCategoryKey == _selectedCategoryKey &&
                    _selectedCategoryKey > 0;

                int selectedKey = _selectedCategoryKey;
                string selectedName = _selectedCategoryName;
                bool selectedIsActive = _selectedCategoryIsActive;

                ShowCategoryGrid();
                SafeRefreshCategories();
                if (wasEditingSelectedCategory)
                    RestoreSelectedCategoryContext(selectedKey, selectedName, selectedIsActive);
                _addCategoryInline?.ConfigureForAdd();
            }
            finally { _isHandlingInlineEvent = false; }
        }

        private void RestoreSelectedCategoryContext(int categoryKey, string categoryName, bool isActive)
        {
            if (categoryKey <= 0)
                return;

            _selectedCategoryKey = categoryKey;
            _selectedCategoryName = categoryName ?? string.Empty;
            _selectedCategoryIsActive = isActive;

            if (btnAddCategoryItem != null)
                btnAddCategoryItem.Visibility = _selectedCategoryIsActive
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            if (txtCategoryItemsTitle != null)
            {
                txtCategoryItemsTitle.Text = string.IsNullOrWhiteSpace(_selectedCategoryName)
                    ? "Category Items"
                    : $"Category Items — {_selectedCategoryName}";
            }

            try { CategoryItemGrid?.Refresh(_selectedCategoryKey, _selectedCategoryName, _categoryItemViewMode); }
            catch { }
        }

        /* =================== Add/Edit Category Item =================== */

        private void btnAddCategoryItem_Click(object sender, RoutedEventArgs e)
        {
            if (_isNavigationLocked) return;
            if (IsPopupOverlayActive) return;

            ShowAddCategoryItemEditor();
        }

        public void ShowEditCategoryItemEditor(int categoryItemId)
        {
            if (_isNavigationLocked) return;
            if (IsPopupOverlayActive) return;

            if (_selectedCategoryKey <= 0) return;
            if (categoryItemId <= 0) return;

            SetActiveEntityForEdit(categoryItemId);
            CreateAndShowEditorOverlay(_selectedCategoryKey, _selectedCategoryName);
        }

        private void ShowAddCategoryItemEditor()
        {
            if (IsPopupOverlayActive) return;
            if (_selectedCategoryKey <= 0) return;
            if (!_selectedCategoryIsActive) return;

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

            _categoryItemEdit.ConfigureForOpen(categoryKey, categoryName);

            AddEditItemOverlayHost.Content = _categoryItemEdit;
            AddEditItemOverlayHost.Visibility = Visibility.Visible;

            SetNavigationLocked(true);
        }

        private void HideAddEditCategoryItem()
        {
            try { _categoryItemEdit?.WipeAllForHostClose(); } catch { }

            AddEditItemOverlayHost.Visibility = Visibility.Collapsed;
            AddEditItemOverlayHost.Content = null;
            _categoryItemEdit = null;

            ClearActiveEntityForAdd();
            SetNavigationLocked(false);
        }

        // Centralize the grid refresh into a single code-callable method on Panel
        public void RequestCategoryItemGridRefresh()
        {
            try
            {
                if (_selectedCategoryKey > 0)
                    CategoryItemGrid?.Refresh(_selectedCategoryKey, _selectedCategoryName, _categoryItemViewMode);
            }
            catch { }
        }

        public void RequestCategoryGridRefresh()
        {
            SafeRefreshCategories();
        }

        private void CategoryItemEdit_Submitted(object? sender, EventArgs e)
        {
            HideAddEditCategoryItem();
            RequestCategoryItemGridRefresh();
        }

        private void CategoryItemEdit_Canceled(object? sender, EventArgs e)
        {
            HideAddEditCategoryItem();
        }

        /* =================== SEDS context helpers =================== */

        private void ClearActiveEntityForAdd()
        {
            try
            {
                SecureEncryptedDataStore.Clear(SedsKey_CurrentEntityId);
                SecureEncryptedDataStore.Clear(SedsKey_CurrentEntityKind);
            }
            catch { }
        }

        private void SetActiveEntityForEdit(int categoryItemId)
        {
            try
            {
                SecureEncryptedDataStore.SetString(SedsKey_CurrentEntityKind, EntityKind_CategoryItem);
                SecureEncryptedDataStore.SetInt32(SedsKey_CurrentEntityId, categoryItemId);
            }
            catch { }
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
            if (_isNavigationLocked || IsEditorOverlayActive || IsPopupOverlayActive)
            {
                UpdateLockdownBanner(true);
                RaiseNavigationLockChanged(true);
                return;
            }

            OverlayHost.Visibility = Visibility.Visible;
            try { LogsOverlay?.Focus(); } catch { }
        }

        private void LogsOverlay_CloseRequested(object? sender, EventArgs e)
        {
            OverlayHost.Visibility = Visibility.Collapsed;
        }

        /* ======================= POPUP OVERLAY (MODAL) ======================= */

        public Task<PopupDialog.PopupResult> ShowPopupAsync(PopupDialog popup)
        {
            if (popup == null) throw new ArgumentNullException(nameof(popup));

            // If one is already active, resolve it as Cancel and replace it.
            if (_popupTcs != null && !_popupTcs.Task.IsCompleted)
            {
                ForceClosePopupIfAny(PopupDialog.PopupResult.Cancel);
            }

            _activePopup = popup;
            _popupTcs = new TaskCompletionSource<PopupDialog.PopupResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                popup.Completed -= Popup_Completed;
                popup.Completed += Popup_Completed;

                PopupOverlayContent.Content = popup;
                PopupOverlayHost.Visibility = Visibility.Visible;

                PopupOverlayHost.Focus();
                Keyboard.Focus(PopupOverlayHost);
            }
            catch
            {
                ForceClosePopupIfAny(PopupDialog.PopupResult.Abort);
            }

            return _popupTcs.Task;
        }

        private void Popup_Completed(PopupDialog.PopupResult result)
        {
            try { HidePopupOverlay(); } catch { }

            try { _popupTcs?.TrySetResult(result); } catch { }
        }

        private void HidePopupOverlay()
        {
            try
            {
                if (_activePopup != null)
                {
                    _activePopup.Completed -= Popup_Completed;
                }
            }
            catch { }

            try
            {
                PopupOverlayHost.Visibility = Visibility.Collapsed;
                PopupOverlayContent.Content = null;
            }
            catch { }

            _activePopup = null;
        }

        private void ForceClosePopupIfAny()
        {
            ForceClosePopupIfAny(PopupDialog.PopupResult.Abort);
        }

        private void ForceClosePopupIfAny(PopupDialog.PopupResult result)
        {
            try { HidePopupOverlay(); } catch { }

            try { _popupTcs?.TrySetResult(result); } catch { }

            _popupTcs = null;
        }

        /* ======================= Helpers ======================= */

        private void SafeRefreshCategories()
        {
            try { CategoryGrid?.Refresh(); }
            catch { }
        }
    }
}
