// File: View/UserControls/Panel.xaml.cs
//
// FULL REWRITE
//
// Purpose:
// - Panel is the "bouncer" for the CategoryItem editor overlay.
// - When overlay is shown, Panel locks left/right navigation.
// - Shows a clear top-of-panel banner while locked.
// - Raises an event to MainWindow so it can disable menu/toolbar navigation too.
// - Hosts a true modal Popup overlay (Accept/Cancel/Abort) that blocks all input behind it.

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Security.Utility.Storage; // SecureEncryptedDataStore (SEDS)
using MWPV.View.UserControls.Popup;

namespace MWPV.View.UserControls
{
    public partial class Panel : UserControl
    {
        private AddCategoryInline? _addCategoryInline;
        private CategoryItemEditorTabs? _categoryItemEdit;

        private bool _isHandlingInlineEvent;

        private int _selectedCategoryKey;
        private string _selectedCategoryName = string.Empty;

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
            //#if DEBUG
            //            Debug.WriteLine("[PANEL][LOADED]");
            //#endif
            WireCategoryGridEvents();
            WireCategoryItemGridEvents();
            WireOverlayEvents();

            SafeRefreshCategories();
        }

        private void Panel_Unloaded(object? sender, RoutedEventArgs e)
        {
            //#if DEBUG
            //            Debug.WriteLine("[PANEL][UNLOADED]");
            //#endif
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
                    //#if DEBUG
                    //                    Debug.WriteLine("[PANEL][HOST-CLOSE] Popup active; forcing Abort.");
                    //#endif
                    ForceClosePopupIfAny(PopupDialog.PopupResult.Abort);
                }

                if (IsEditorOverlayActive && AddEditItemOverlayHost.Content is CategoryItemEditorTabs tabs)
                {
                    //#if DEBUG
                    //                    Debug.WriteLine("[PANEL][HOST-CLOSE] Forwarding wipe to CategoryItemEditorTabs.");
                    //#endif
                    tabs.WipeAllForHostClose();
                }
            }
            catch (Exception ex)
            {
                //#if DEBUG
                //                Debug.WriteLine("[PANEL][HOST-CLOSE][ERR] " + ex);
                //#endif
            }
        }

        /* =================== Navigation Lock + Banner =================== */

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

            UpdateLockdownBanner(locked);

            //#if DEBUG
            //            Debug.WriteLine($"[PANEL][NAV-LOCK] locked={locked}");
            //#endif

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

            txtCategoryItemsTitle.Text = "Category Items";
            btnAddCategoryItem.Visibility = Visibility.Collapsed;

            //#if DEBUG
            //            Debug.WriteLine("[PANEL][LEFT] CategoryGrid events wired.");
            //#endif
        }

        private void UnwireCategoryGridEvents()
        {
            if (CategoryGrid == null) return;

            CategoryGrid.SelectedCategoryChanged -= CategoryGrid_SelectedCategoryChanged;

            //#if DEBUG
            //            Debug.WriteLine("[PANEL][LEFT] CategoryGrid events unwired.");
            //#endif
        }

        private void CategoryGrid_SelectedCategoryChanged(object sender, RoutedEventArgs e)
        {
            if (_isNavigationLocked)
            {
                //#if DEBUG
                //                Debug.WriteLine("[PANEL][LEFT→RIGHT] Selection ignored (navigation locked).");
                //#endif
                return;
            }

            if (IsPopupOverlayActive)
            {
                //#if DEBUG
                //                Debug.WriteLine("[PANEL][LEFT→RIGHT] Selection ignored (popup modal active).");
                //#endif
                return;
            }

            var sel = CategoryGrid.GetSelectedCategory(e);
            _selectedCategoryKey = sel.Key;
            _selectedCategoryName = sel.Name ?? string.Empty;

            //#if DEBUG
            //            Debug.WriteLine($"[PANEL][LEFT→RIGHT] Category selected: key={_selectedCategoryKey}, name='{_selectedCategoryName}'");
            //#endif

            btnAddCategoryItem.Visibility = Visibility.Visible;
            txtCategoryItemsTitle.Text = string.IsNullOrWhiteSpace(_selectedCategoryName)
                ? "Category Items"
                : $"Category Items — {_selectedCategoryName}";

            try { CategoryItemGrid?.Refresh(_selectedCategoryKey, _selectedCategoryName); }
            catch (Exception ex)
            {
                //#if DEBUG
                //                Debug.WriteLine($"[PANEL][RIGHT][REFRESH][ERR] {ex}");
                //#endif
            }
        }

        /* =================== Category Item Grid (pills) =================== */

        private void WireCategoryItemGridEvents()
        {
            if (CategoryItemGrid == null) return;

            CategoryItemGrid.EditRequested -= CategoryItemGrid_EditRequested;
            CategoryItemGrid.EditRequested += CategoryItemGrid_EditRequested;

            //#if DEBUG
            //            Debug.WriteLine("[PANEL][RIGHT] CategoryItemGrid events wired.");
            //#endif
        }

        private void UnwireCategoryItemGridEvents()
        {
            if (CategoryItemGrid == null) return;

            CategoryItemGrid.EditRequested -= CategoryItemGrid_EditRequested;

            //#if DEBUG
            //            Debug.WriteLine("[PANEL][RIGHT] CategoryItemGrid events unwired.");
            //#endif
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
            if (IsPopupOverlayActive) return;

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
            if (IsPopupOverlayActive) return;

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

        // SPECIFIC CHANGE ONLY:
        // - Centralize the grid refresh into a single code-callable method on Panel
        // - This can be invoked by user code (tabs exit) without relying on a button click event.
        public void RequestCategoryItemGridRefresh()
        {
            try
            {
                if (_selectedCategoryKey > 0)
                    CategoryItemGrid?.Refresh(_selectedCategoryKey, _selectedCategoryName);
            }
            catch { }
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
                //#if DEBUG
                //                Debug.WriteLine("[PANEL][LOGS] Blocked: editor overlay active OR popup modal active.");
                //#endif
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

        /// <summary>
        /// Shows a true modal popup overlay (blocks all input behind it) and returns the result.
        /// Caller typically does:
        /// - Accept => proceed (insert)
        /// - Cancel => do nothing (no insert)
        /// - Abort  => terminate flow safely
        /// </summary>
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
            catch (Exception ex)
            {
                //#if DEBUG
                //                Debug.WriteLine("[PANEL][POPUP][SHOW][ERR] " + ex);
                //#endif
                ForceClosePopupIfAny(PopupDialog.PopupResult.Abort);
            }

            return _popupTcs.Task;
        }

        private void Popup_Completed(PopupDialog.PopupResult result)
        {
            // Close UI first, then complete the Task.
            try
            {
                HidePopupOverlay();
            }
            catch { }

            try
            {
                _popupTcs?.TrySetResult(result);
            }
            catch { }
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
            try
            {
                HidePopupOverlay();
            }
            catch { }

            try
            {
                _popupTcs?.TrySetResult(result);
            }
            catch { }

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
