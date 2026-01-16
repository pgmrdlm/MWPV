// File: View/UserControls/CategoryItemEditorTabs.xaml.cs
//
// FULL REWRITE (Guardrails-first; INSERT for new items, UPDATE for existing items)
// -----------------------------------------------------------------------------
// Ground rules (kept + extended):
// 1) SEDS tells us ONLY whether the item exists (PK present).
//    - If PK present => existing item => open in VIEW by default (panel decides).
// 2) This file stays out of crypto. Services do AES.
// 3) Inserts happen only once: new item (no PK) => insert => set SEDS PK.
// 4) Updates happen only on explicit Save actions (NOT on tab-leave).
// 5) Never clear form on Loaded if PK exists.
// 6) Tab switching:
//    - Leaving Basic requires validation.
//    - If NEW item, we insert-on-leave (so downstream tabs can rely on PK).
//    - If EXISTING item, we do NOT write on leave (save is explicit).
// 7) Password history updates for EXISTING items:
//    - Only occur on explicit Save.
//    - Uses CategoryItemService.InsertPasswordHistoryForExistingItem(...)
// 8) Duplicate password warning (GLOBAL):
//    - Service throws DuplicatePasswordWarningException
//    - UI catches, shows OUR PopupDialog (themed) using Panel.xaml overlay host
//    - Accept => retry with allowDuplicate:true
//    - Cancel => stop, no save
// -----------------------------------------------------------------------------
//
// DEBUG additions:
// - Logs bookmark-only state and which persistence path is used.
// -----------------------------------------------------------------------------


using MWPV.Services;
using MWPV.View.UserControls.CategoryItems;
using MWPV.View.UserControls.Popup;
using Security.Utility.Storage; // SecureEncryptedDataStore (SEDS)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;     // VisualTreeHelper
using System.Windows.Threading;

namespace MWPV.View.UserControls
{
    public partial class CategoryItemEditorTabs : UserControl
    {
        public event EventHandler? Submitted;
        public event EventHandler? Canceled;
        public event EventHandler<string>? PasswordValidationFailed;

        private int _categoryKey;
        private string _categoryName = string.Empty;

        // Existence only (PK present)
        private bool _hasPersistedId;

        private bool _panelsHooked;
        private bool _handlingTabSelection;
        private int _lastTabIndex;
        private bool _isClosing;

        public IReadOnlyList<CategoryItemBankCardsPanel.BankCardRow> BankCardsDraftRows { get; private set; }
            = Array.Empty<CategoryItemBankCardsPanel.BankCardRow>();

        private const int TabIndexBasic = 0;
        private const int TabIndexBankCards = 1;

        private const string EntityKind_CategoryItem = "CategoryItem";
        private static readonly string SedsKey_EntityKind = SecureEncryptedDataStore.ContextKeys.CurrentEntityKind;
        private static readonly string SedsKey_EntityId = SecureEncryptedDataStore.ContextKeys.CurrentEntityId;

        public CategoryItemEditorTabs()
        {
            InitializeComponent();

            Loaded += CategoryItemEditorTabs_Loaded;
            Unloaded += CategoryItemEditorTabs_Unloaded;
        }

        /* ======================= SEDS helpers ======================= */

        private static int? TryGetActiveCategoryItemId()
        {
            try
            {
                if (!SecureEncryptedDataStore.TryGetBytes(SedsKey_EntityKind, out var kindBytes) || kindBytes.Length == 0)
                    return null;

                string kind;
                try { kind = Encoding.UTF8.GetString(kindBytes); }
                finally { Array.Clear(kindBytes, 0, kindBytes.Length); }

                if (!string.Equals(kind, EntityKind_CategoryItem, StringComparison.Ordinal))
                    return null;

                if (SecureEncryptedDataStore.TryGetInt32(SedsKey_EntityId, out int id) && id > 0)
                    return id;

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static bool HasPersistedIdFromSeds() => TryGetActiveCategoryItemId().HasValue;

        private static void SetSedsContextForCategoryItem(int id)
        {
            SecureEncryptedDataStore.SetString(SedsKey_EntityKind, EntityKind_CategoryItem);
            SecureEncryptedDataStore.SetInt32(SedsKey_EntityId, id);
        }

        private static void ClearSedsContext()
        {
            try { SecureEncryptedDataStore.Clear(SedsKey_EntityId); } catch { }
            try { SecureEncryptedDataStore.Clear(SedsKey_EntityKind); } catch { }
        }

        /* ======================= Public Open API ======================= */

        public void ConfigureForOpen(int categoryKey, string categoryName)
        {
            _categoryKey = categoryKey;
            _categoryName = categoryName;

            _hasPersistedId = HasPersistedIdFromSeds();

#if DEBUG
            var id = TryGetActiveCategoryItemId();
            Debug.WriteLine($"[ITEM-TABS] ConfigureForOpen: catKey={categoryKey}, name='{categoryName}', exists={_hasPersistedId}, activeId={(id.HasValue ? id.Value : 0)}");
#endif

            InitializeUiForOpen();
        }

        public void ConfigureForAdd(int categoryKey, string categoryName)
        {
            _categoryKey = categoryKey;
            _categoryName = categoryName;

            ClearSedsContext();
            _hasPersistedId = false;

#if DEBUG
            Debug.WriteLine($"[ITEM-TABS] ConfigureForAdd: catKey={categoryKey}, name='{categoryName}', exists=false (SEDS cleared)");
#endif

            InitializeUiForOpen();
        }

        public void ConfigureForEdit(int categoryKey, string categoryName, object existingItem)
        {
            // Existence comes from SEDS, not from this method.
            _categoryKey = categoryKey;
            _categoryName = categoryName;

            _hasPersistedId = HasPersistedIdFromSeds();

#if DEBUG
            var id = TryGetActiveCategoryItemId();
            Debug.WriteLine($"[ITEM-TABS] ConfigureForEdit: catKey={categoryKey}, name='{categoryName}', exists={_hasPersistedId}, activeId={(id.HasValue ? id.Value : 0)} (SEDS-derived existence)");
#endif

            InitializeUiForOpen();

            // Future: load existing item into panels, then force view mode in BasicPanel itself.
        }

        private void InitializeUiForOpen()
        {
            HookPanelsOnce();

            if (ItemTabs != null)
            {
                ItemTabs.SelectionChanged -= ItemTabs_SelectionChanged;
                ItemTabs.SelectionChanged += ItemTabs_SelectionChanged;

                if (ItemTabs.SelectedIndex < 0)
                    ItemTabs.SelectedIndex = TabIndexBasic;

                _lastTabIndex = ItemTabs.SelectedIndex;
            }

            _hasPersistedId = HasPersistedIdFromSeds();

            if (_hasPersistedId)
            {
                // Existing item: do NOT clear. Default is "view" handled by BasicPanel itself.
                try { BasicPanel?.ResetUiState(); } catch { }
            }
            else
            {
                // New item: clear + reset.
                try { BasicPanel?.ClearForm(); } catch { }
                try { BasicPanel?.ResetUiState(); } catch { }
            }

            SetStatus("");
        }

        /* ======================= Lifecycle ======================= */

        private void CategoryItemEditorTabs_Loaded(object? sender, RoutedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[ITEM-TABS] Loaded");
#endif
            HookPanelsOnce();

            if (ItemTabs != null)
            {
                ItemTabs.SelectionChanged -= ItemTabs_SelectionChanged;
                ItemTabs.SelectionChanged += ItemTabs_SelectionChanged;
                _lastTabIndex = ItemTabs.SelectedIndex;
            }

            _hasPersistedId = HasPersistedIdFromSeds();
            InitializeUiForOpen();
            SetStatus("");
        }

        private void CategoryItemEditorTabs_Unloaded(object? sender, RoutedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[ITEM-TABS] Unloaded");
#endif
            UnhookPanels();

            if (ItemTabs != null)
                ItemTabs.SelectionChanged -= ItemTabs_SelectionChanged;

            try
            {
                BasicPanel?.WipeAllForHostClose();
                BankCardsPanel?.WipeAllForHostClose();
            }
            catch { }

            try { ClearSedsContext(); } catch { }

            SetStatus("");
        }

        /* ======================= Host API ======================= */

        public void WipeAllForHostClose()
        {
            if (_isClosing)
                return;

            _isClosing = true;

#if DEBUG
            Debug.WriteLine("[ITEM-TABS] WipeAllForHostClose ENTER");
#endif
            try
            {
                TryPreparePanelsForHostClose();
                SetStatus("");
            }
            finally
            {
                try { ClearSedsContext(); } catch { }
#if DEBUG
                Debug.WriteLine("[ITEM-TABS] WipeAllForHostClose EXIT");
#endif
            }
        }

        private void TryPreparePanelsForHostClose()
        {
            try { BasicPanel?.WipeAllForHostClose(); }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine($"[ITEM-TABS] BasicPanel.WipeAllForHostClose failed: {ex}");
#endif
            }

            try { BankCardsPanel?.WipeAllForHostClose(); }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine($"[ITEM-TABS] BankCardsPanel.WipeAllForHostClose failed: {ex}");
#endif
            }
        }

        /* ======================= Persistence ======================= */

        private enum PersistTrigger
        {
            LeaveBasicTab,
            SaveAndExit
        }

        private bool TryPersistBasicIfNeeded(PersistTrigger trigger, bool isBookmarkOnly)
        {
            if (_isClosing)
                return true;

            if (BasicPanel == null)
                return true;

            var activeId = TryGetActiveCategoryItemId();
            bool isExisting = _hasPersistedId && activeId.HasValue && activeId.Value > 0;

#if DEBUG
            Debug.WriteLine($"[ITEM-TABS][PERSIST] ENTER trigger={trigger} bookmarkOnly={isBookmarkOnly} hasPersisted={_hasPersistedId} activeId={(activeId.HasValue ? activeId.Value : 0)} isExisting={isExisting}");
#endif

            // =========================
            // EXISTING ITEM
            // =========================
            if (isExisting)
            {
                // Guardrail: NO update on tab leave.
                if (trigger == PersistTrigger.LeaveBasicTab)
                {
#if DEBUG
                    Debug.WriteLine($"[ITEM-TABS][PERSIST] Existing item (id={activeId!.Value}) leave-tab: no write. bookmarkOnly={isBookmarkOnly}");
#endif
                    return true;
                }

                // SaveAndExit: UPDATE basic fields, then (optionally) insert password history.
                try
                {
                    long itemId = activeId!.Value;

#if DEBUG
                    Debug.WriteLine($"[ITEM-TABS][UPDATE] Existing SaveAndExit BEGIN itemId={itemId} bookmarkOnly={isBookmarkOnly}");
#endif

                    string name = BasicPanel.GetItemNameTrim();
                    string? desc = BasicPanel.GetDescriptionTrimOrNull();
                    string? username = BasicPanel.GetUsernameTrimOrNull();
                    string? url = BasicPanel.GetUrlTrimOrNull();

                    string? emailPlain = BasicPanel.GetEmailTrimOrNull();
                    string? phonePlain = BasicPanel.GetPhoneTrimOrNull();
                    string? pinPlain = BasicPanel.GetPinTrimOrNull();

                    int? isActive = 1;
                    int? bookMarkOnly = isBookmarkOnly ? 1 : 0;

                    int rows = CategoryItemService.UpdateCategoryItemBasic(
                        itemId: itemId,
                        name: name,
                        description: desc,
                        username: username,
                        signInUrl: url,
                        bookMarkOnly: bookMarkOnly,
                        accountEmailPlain: emailPlain,
                        accountPhonePlain: phonePlain,
                        pinPlain: pinPlain,
                        isActive: isActive);

                    if (rows <= 0)
                    {
                        SetStatus("Update failed (0 rows affected).");
#if DEBUG
                        Debug.WriteLine($"[ITEM-TABS][UPDATE] FAILED itemId={itemId} rowsAffected=0 bookmarkOnly={isBookmarkOnly}");
#endif
                        return false;
                    }

#if DEBUG
                    Debug.WriteLine($"[ITEM-TABS][UPDATE] OK itemId={itemId} rowsAffected={rows} bookmarkOnly={isBookmarkOnly}");
#endif

                    // Ensure SEDS stays correct.
                    SetSedsContextForCategoryItem(activeId.Value);
                    _hasPersistedId = true;

                    // Password History Insert (EXISTING ITEM) - ONLY ON SAVE
                    if (isBookmarkOnly)
                    {
#if DEBUG
                        Debug.WriteLine($"[ITEM-TABS][PW-HIST] SKIP (bookmark-only) itemId={itemId}");
#endif
                    }
                    else
                    {
                        string? pw = BasicPanel.GetPasswordPlainOrNull();

#if DEBUG
                        Debug.WriteLine($"[ITEM-TABS][PW-HIST] Existing SaveAndExit passwordCheck itemId={itemId} pwIsNull={(pw == null)} pwIsWhite={(string.IsNullOrWhiteSpace(pw))}");
#endif

                        if (!string.IsNullOrWhiteSpace(pw))
                        {
                            long pwHistId;

                            try
                            {
                                pwHistId = CategoryItemService.InsertPasswordHistoryForExistingItem(
                                    itemId: itemId,
                                    passwordPlain: pw,
                                    isBookmarkOnly: false,
                                    allowDuplicate: false);
                            }
                            catch (CategoryItemService.DuplicatePasswordWarningException)
                            {
#if DEBUG
                                Debug.WriteLine($"[ITEM-TABS][DUP-PW] Duplicate warning (existing itemId={itemId}). Prompting user.");
#endif

                                if (!PromptDuplicatePasswordAccept())
                                {
#if DEBUG
                                    Debug.WriteLine("[ITEM-TABS][DUP-PW] User canceled duplicate warning (existing). Aborting save.");
#endif
                                    SetStatus("Save canceled (duplicate password warning).");
                                    return false;
                                }

#if DEBUG
                                Debug.WriteLine("[ITEM-TABS][DUP-PW] User accepted duplicate warning (existing). Retrying allowDuplicate=true.");
#endif

                                pwHistId = CategoryItemService.InsertPasswordHistoryForExistingItem(
                                    itemId: itemId,
                                    passwordPlain: pw,
                                    isBookmarkOnly: false,
                                    allowDuplicate: true);
                            }

                            if (pwHistId <= 0)
                            {
                                SetStatus("Saved Basic fields, but password history insert failed.");
#if DEBUG
                                Debug.WriteLine($"[ITEM-TABS][PW-HIST][INSERT] FAILED itemId={itemId} pwHistId={pwHistId}");
#endif
                                return false;
                            }

#if DEBUG
                            Debug.WriteLine($"[ITEM-TABS][PW-HIST][INSERT] OK itemId={itemId} pwHistId={pwHistId}");
#endif
                        }
                        else
                        {
#if DEBUG
                            Debug.WriteLine($"[ITEM-TABS][PW-HIST] SKIP (empty/no change) itemId={itemId}");
#endif
                        }
                    }

                    SetStatus("");
                    return true;
                }
                catch (Exception ex)
                {
#if DEBUG
                    Debug.WriteLine($"[ITEM-TABS][SAVE][EXISTING] FAILED: {ex}");
#endif
                    SetStatus("Save failed. See debug output.");
                    return false;
                }
            }

            // =========================
            // NEW ITEM
            // =========================
            if (_hasPersistedId && !activeId.HasValue)
            {
#if DEBUG
                Debug.WriteLine("[ITEM-TABS][PERSIST] _hasPersistedId=true but SEDS has no valid ItemId. Treating as NEW.");
#endif
                _hasPersistedId = false;
            }

            try
            {
#if DEBUG
                Debug.WriteLine($"[ITEM-TABS][INSERT] New item persist BEGIN trigger={trigger} bookmarkOnly={isBookmarkOnly} catKey={_categoryKey} catName='{_categoryName}'");
#endif

                string name = BasicPanel.GetItemNameTrim();
                string? desc = BasicPanel.GetDescriptionTrimOrNull();
                string? username = BasicPanel.GetUsernameTrimOrNull();
                string? url = BasicPanel.GetUrlTrimOrNull();

                string? emailPlain = BasicPanel.GetEmailTrimOrNull();
                string? phonePlain = BasicPanel.GetPhoneTrimOrNull();
                string? pinPlain = BasicPanel.GetPinTrimOrNull();

                int? isActive = 1;

                long newId;

                if (isBookmarkOnly)
                {
#if DEBUG
                    Debug.WriteLine("[ITEM-TABS][INSERT] PATH=BookmarkOnly => InsertCategoryItemOnly()");
#endif
                    newId = CategoryItemService.InsertCategoryItemOnly(
                        categoryKey: _categoryKey,
                        name: name,
                        description: desc,
                        username: username,
                        signInUrl: url,
                        accountEmailPlain: emailPlain,
                        accountPhonePlain: phonePlain,
                        pinPlain: pinPlain,
                        isActive: isActive
                    );
                }
                else
                {
                    string? passwordPlain = BasicPanel.GetPasswordPlainOrNull();

#if DEBUG
                    Debug.WriteLine($"[ITEM-TABS][INSERT] PATH=WithPassword => InsertCategoryItemWithPasswordHistory() pwIsNull={(passwordPlain == null)} pwIsWhite={(string.IsNullOrWhiteSpace(passwordPlain))}");
#endif

                    try
                    {
                        newId = CategoryItemService.InsertCategoryItemWithPasswordHistory(
                            categoryKey: _categoryKey,
                            name: name,
                            description: desc,
                            username: username,
                            signInUrl: url,
                            accountEmailPlain: emailPlain,
                            accountPhonePlain: phonePlain,
                            pinPlain: pinPlain,
                            isActive: isActive,
                            passwordPlain: passwordPlain,
                            isBookmarkOnly: false,
                            allowDuplicate: false);
                    }
                    catch (CategoryItemService.DuplicatePasswordWarningException)
                    {
#if DEBUG
                        Debug.WriteLine("[ITEM-TABS][DUP-PW] Duplicate warning (new item). Prompting user.");
#endif

                        if (!PromptDuplicatePasswordAccept())
                        {
#if DEBUG
                            Debug.WriteLine("[ITEM-TABS][DUP-PW] User canceled duplicate warning (new). Aborting insert.");
#endif
                            SetStatus("Save canceled (duplicate password warning).");
                            return false;
                        }

#if DEBUG
                        Debug.WriteLine("[ITEM-TABS][DUP-PW] User accepted duplicate warning (new). Retrying allowDuplicate=true.");
#endif

                        newId = CategoryItemService.InsertCategoryItemWithPasswordHistory(
                            categoryKey: _categoryKey,
                            name: name,
                            description: desc,
                            username: username,
                            signInUrl: url,
                            accountEmailPlain: emailPlain,
                            accountPhonePlain: phonePlain,
                            pinPlain: pinPlain,
                            isActive: isActive,
                            passwordPlain: passwordPlain,
                            isBookmarkOnly: false,
                            allowDuplicate: true);
                    }
                }

                if (newId <= 0)
                {
                    SetStatus("Insert failed (no ItemId returned).");
#if DEBUG
                    Debug.WriteLine($"[ITEM-TABS][INSERT] FAILED newId={newId} bookmarkOnly={isBookmarkOnly}");
#endif
                    return false;
                }

                if (newId > int.MaxValue)
                {
                    SetStatus("Insert failed (ItemId overflow).");
#if DEBUG
                    Debug.WriteLine($"[ITEM-TABS][INSERT] FAILED New ItemId={newId} exceeds Int32.MaxValue; cannot store in SEDS Int32.");
#endif
                    return false;
                }

                SetSedsContextForCategoryItem((int)newId);
                _hasPersistedId = true;

#if DEBUG
                Debug.WriteLine($"[ITEM-TABS][INSERT] OK newId={newId} bookmarkOnly={isBookmarkOnly} => SEDS set (kind={EntityKind_CategoryItem})");
#endif

                if (trigger == PersistTrigger.LeaveBasicTab)
                    SetStatus("");

                return true;
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine($"[ITEM-TABS][INSERT][NEW] FAILED: {ex}");
#endif
                SetStatus("Insert failed. See debug output.");
                return false;
            }
        }

        /* ======================= Duplicate Password Popup (OUR themed PopupDialog) ======================= */

        private bool PromptDuplicatePasswordAccept()
        {
            // Message is intentionally generic (no hints, no item/category/site).
            const string title = "Duplicate Password Warning";
            const string body =
                "This password is currently used or has been previously used.\n\n" +
                "Accept to continue.";

            // We show it using Panel.xaml's modal overlay:
            //   Border x:Name="PopupOverlayHost"
            //   ContentControl x:Name="PopupOverlayContent"
            try
            {
                var hostPanel = FindPanelHost();
                if (hostPanel == null)
                {
#if DEBUG
                    Debug.WriteLine("[ITEM-TABS][POPUP] Host Panel not found. Falling back to MessageBox.");
#endif
                    var r = MessageBox.Show(body, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                    return r == MessageBoxResult.OK;
                }

                var overlayHost = hostPanel.FindName("PopupOverlayHost") as Border;
                var overlayContent = hostPanel.FindName("PopupOverlayContent") as ContentControl;

                if (overlayHost == null || overlayContent == null)
                {
#if DEBUG
                    Debug.WriteLine("[ITEM-TABS][POPUP] PopupOverlayHost/PopupOverlayContent not found in Panel. Falling back to MessageBox.");
#endif
                    var r = MessageBox.Show(body, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                    return r == MessageBoxResult.OK;
                }

                bool accepted = false;

                var popup = new PopupDialog();
                popup.ConfigureWarningAcceptCancel(title, body);

                // Modal loop (no Window): nested dispatcher frame
                var frame = new DispatcherFrame();

                popup.Completed += result =>
                {
                    accepted = (result == PopupDialog.PopupResult.Accept);

                    try { overlayContent.Content = null; } catch { }
                    try { overlayHost.Visibility = Visibility.Collapsed; } catch { }

                    frame.Continue = false;
                };

                overlayContent.Content = popup;
                overlayHost.Visibility = Visibility.Visible;

                // Focus it (feels "forced")
                popup.Focus();
                Keyboard.Focus(popup);

                Dispatcher.PushFrame(frame);

                return accepted;
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine($"[ITEM-TABS][POPUP] Failed to show PopupDialog: {ex}");
#endif
                var r = MessageBox.Show(body, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                return r == MessageBoxResult.OK;
            }
        }

        private MWPV.View.UserControls.Panel? FindPanelHost()
        {
            // Walk up the visual tree to locate our main "Panel" usercontrol host.
            // This avoids name collision with System.Windows.Controls.Panel by fully qualifying MWPV.View.UserControls.Panel.
            try
            {
                DependencyObject? cur = this;

                while (cur != null)
                {
                    if (cur is MWPV.View.UserControls.Panel p)
                        return p;

                    cur = VisualTreeHelper.GetParent(cur);
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /* ======================= Panel hooks ======================= */

        private void HookPanelsOnce()
        {
            if (_panelsHooked)
                return;

            if (BasicPanel != null)
            {
                BasicPanel.SaveRequested -= BasicPanel_SaveRequested;
                BasicPanel.CancelRequested -= BasicPanel_CancelRequested;
                BasicPanel.PasswordValidationFailed -= BasicPanel_PasswordValidationFailed;

                BasicPanel.SaveRequested += BasicPanel_SaveRequested;
                BasicPanel.CancelRequested += BasicPanel_CancelRequested;
                BasicPanel.PasswordValidationFailed += BasicPanel_PasswordValidationFailed;
            }

            if (BankCardsPanel != null)
            {
                BankCardsPanel.SaveAndExitRequested -= BankCardsPanel_SaveAndExitRequested;
                BankCardsPanel.CancelAndExitRequested -= BankCardsPanel_CancelAndExitRequested;

                BankCardsPanel.SaveAndExitRequested += BankCardsPanel_SaveAndExitRequested;
                BankCardsPanel.CancelAndExitRequested += BankCardsPanel_CancelAndExitRequested;
            }

            _panelsHooked = true;
        }

        private void UnhookPanels()
        {
            if (!_panelsHooked)
                return;

            if (BasicPanel != null)
            {
                BasicPanel.SaveRequested -= BasicPanel_SaveRequested;
                BasicPanel.CancelRequested -= BasicPanel_CancelRequested;
                BasicPanel.PasswordValidationFailed -= BasicPanel_PasswordValidationFailed;
            }

            if (BankCardsPanel != null)
            {
                BankCardsPanel.SaveAndExitRequested -= BankCardsPanel_SaveAndExitRequested;
                BankCardsPanel.CancelAndExitRequested -= BankCardsPanel_CancelAndExitRequested;
            }

            _panelsHooked = false;
        }

        private void BasicPanel_PasswordValidationFailed(object? sender, string message)
        {
            PasswordValidationFailed?.Invoke(this, message);
        }

        private void BasicPanel_SaveRequested(object? sender, EventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[ITEM-TABS] BasicPanel SaveRequested");
#endif
            SetStatus("");

            if (BasicPanel == null)
                return;

            BasicPanel.NormalizeUsernameFromEmailIfEmpty();

            if (!BasicPanel.TryValidateAllForSubmit(out bool isBookmarkOnly, out bool okName, out bool okPassword, out bool okPin, out bool okEmail, out bool okPhone))
            {
#if DEBUG
                Debug.WriteLine($"[ITEM-TABS][SAVE] Validation FAILED bookmarkOnly={isBookmarkOnly} okName={okName} okPw={okPassword} okPin={okPin} okEmail={okEmail} okPhone={okPhone}");
#endif
                if (ItemTabs != null && ItemTabs.SelectedIndex != TabIndexBasic)
                    ItemTabs.SelectedIndex = TabIndexBasic;

                BasicPanel.FocusFirstError(okName, okPassword, okPin, okEmail, okPhone, isBookmarkOnly);
                SetStatus("Fix the highlighted errors before saving.");
                return;
            }

#if DEBUG
            Debug.WriteLine($"[ITEM-TABS][SAVE] Validation OK bookmarkOnly={isBookmarkOnly}");
#endif

            if (!TryPersistBasicIfNeeded(PersistTrigger.SaveAndExit, isBookmarkOnly))
            {
                if (ItemTabs != null && ItemTabs.SelectedIndex != TabIndexBasic)
                    ItemTabs.SelectedIndex = TabIndexBasic;

                return;
            }

            WipeAllForHostClose();
            Submitted?.Invoke(this, EventArgs.Empty);
        }

        private void BasicPanel_CancelRequested(object? sender, EventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[ITEM-TABS] BasicPanel CancelRequested");
#endif
            SetStatus("");

            WipeAllForHostClose();
            Canceled?.Invoke(this, EventArgs.Empty);
        }

        /* ======================= Bank Cards integration ======================= */

        private void BankCardsPanel_SaveAndExitRequested(object? sender, CategoryItemBankCardsPanel.BankCardsCommitEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[ITEM-TABS] BankCardsPanel SaveAndExitRequested");
#endif
            BankCardsDraftRows = (e.Rows ?? Array.Empty<CategoryItemBankCardsPanel.BankCardRow>()).ToList();
            SetStatus("");

            if (BasicPanel == null)
                return;

            BasicPanel.NormalizeUsernameFromEmailIfEmpty();

            if (!BasicPanel.TryValidateAllForSubmit(out bool isBookmarkOnly, out bool okName, out bool okPassword, out bool okPin, out bool okEmail, out bool okPhone))
            {
#if DEBUG
                Debug.WriteLine($"[ITEM-TABS][BANK-SAVE] Validation FAILED bookmarkOnly={isBookmarkOnly} okName={okName} okPw={okPassword} okPin={okPin} okEmail={okEmail} okPhone={okPhone}");
#endif
                if (ItemTabs != null)
                    ItemTabs.SelectedIndex = TabIndexBasic;

                BasicPanel.FocusFirstError(okName, okPassword, okPin, okEmail, okPhone, isBookmarkOnly);
                SetStatus("Bank Cards are staged, fix Basic tab errors before saving.");
                return;
            }

#if DEBUG
            Debug.WriteLine($"[ITEM-TABS][BANK-SAVE] Validation OK bookmarkOnly={isBookmarkOnly} rowsStaged={BankCardsDraftRows.Count}");
#endif

            if (!TryPersistBasicIfNeeded(PersistTrigger.SaveAndExit, isBookmarkOnly))
            {
                if (ItemTabs != null)
                    ItemTabs.SelectedIndex = TabIndexBasic;

                return;
            }

            WipeAllForHostClose();
            Submitted?.Invoke(this, EventArgs.Empty);
        }

        private void BankCardsPanel_CancelAndExitRequested(object? sender, EventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[ITEM-TABS] BankCardsPanel CancelAndExitRequested");
#endif
            WipeAllForHostClose();
            Canceled?.Invoke(this, EventArgs.Empty);
        }

        /* ======================= Tab switching behavior ======================= */

        private void ItemTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_handlingTabSelection)
                return;

            if (ItemTabs == null)
                return;

            int newIndex = ItemTabs.SelectedIndex;
            int oldIndex = _lastTabIndex;

            // Leaving Basic -> must validate, and if NEW, persist insert-on-leave
            if (!_isClosing && oldIndex == TabIndexBasic && newIndex != TabIndexBasic)
            {
#if DEBUG
                Debug.WriteLine($"[ITEM-TABS][TAB] Leaving BASIC => newIndex={newIndex}");
#endif
                bool allowLeaveBasic = TryValidateAndPersistOnLeaveBasic();
                if (!allowLeaveBasic)
                {
#if DEBUG
                    Debug.WriteLine("[ITEM-TABS][TAB] Leave BASIC BLOCKED -> force back to Basic.");
#endif
                    _handlingTabSelection = true;
                    try { ItemTabs.SelectedIndex = TabIndexBasic; }
                    finally { _handlingTabSelection = false; }

                    _lastTabIndex = TabIndexBasic;
                    return;
                }
            }

            // Leaving BankCards -> let panel auto-commit/wipe draft row state
            if (oldIndex == TabIndexBankCards && newIndex != TabIndexBankCards)
            {
#if DEBUG
                Debug.WriteLine($"[ITEM-TABS][TAB] Leaving BANKCARDS => newIndex={newIndex}");
#endif
                bool allowLeave = true;

                try
                {
                    _handlingTabSelection = true;
                    if (BankCardsPanel != null)
                        allowLeave = BankCardsPanel.TryAutoCommitAndWipe();
                }
                finally
                {
                    _handlingTabSelection = false;
                }

                if (!allowLeave)
                {
#if DEBUG
                    Debug.WriteLine("[ITEM-TABS][TAB] Leave BANKCARDS BLOCKED -> force back to BankCards.");
#endif
                    _handlingTabSelection = true;
                    try { ItemTabs.SelectedIndex = TabIndexBankCards; }
                    finally { _handlingTabSelection = false; }

                    _lastTabIndex = TabIndexBankCards;
                    return;
                }
            }

            _lastTabIndex = newIndex;
        }

        private bool TryValidateAndPersistOnLeaveBasic()
        {
            SetStatus("");

            if (BasicPanel == null)
                return true;

            BasicPanel.NormalizeUsernameFromEmailIfEmpty();

            if (!BasicPanel.TryValidateAllForSubmit(out bool isBookmarkOnly, out bool okName, out bool okPassword, out bool okPin, out bool okEmail, out bool okPhone))
            {
#if DEBUG
                Debug.WriteLine($"[ITEM-TABS][LEAVE-BASIC] Validation FAILED bookmarkOnly={isBookmarkOnly} okName={okName} okPw={okPassword} okPin={okPin} okEmail={okEmail} okPhone={okPhone}");
#endif
                BasicPanel.FocusFirstError(okName, okPassword, okPin, okEmail, okPhone, isBookmarkOnly);
                SetStatus("Cannot leave Basic tab, fix highlighted errors first.");
                return false;
            }

#if DEBUG
            Debug.WriteLine($"[ITEM-TABS][LEAVE-BASIC] Validation OK bookmarkOnly={isBookmarkOnly} -> persist if needed");
#endif

            // Leaving Basic should INSERT only for NEW items. Existing items do not write here.
            if (!TryPersistBasicIfNeeded(PersistTrigger.LeaveBasicTab, isBookmarkOnly))
            {
#if DEBUG
                Debug.WriteLine($"[ITEM-TABS][LEAVE-BASIC] Persist FAILED bookmarkOnly={isBookmarkOnly}");
#endif
                return false;
            }

#if DEBUG
            Debug.WriteLine($"[ITEM-TABS][LEAVE-BASIC] Persist OK bookmarkOnly={isBookmarkOnly}");
#endif
            return true;
        }

        /* ======================= Status line ======================= */

        private void SetStatus(string text)
        {
            try
            {
                if (txtStatusLine == null)
                    return;

                txtStatusLine.Text = text ?? string.Empty;
            }
            catch
            {
                // swallow (status line must never crash the editor)
            }
        }
    }
}
