// File: View/UserControls/CategoryItemEditorTabs.xaml.cs
//
// FULL REWRITE (AES-only intent, NO UI crypto)
// - Mode is PK-driven via SEDS.
//   - If SEDS(kind=="CategoryItem" && id>0) => EDIT/VIEW
//   - Else => ADD
//
// - UI does NOT perform encryption anymore.
//   - BasicPanel returns plaintext strings.
//   - Service encrypts with AES via CategoryItemService.
//
// - Do NOT clear the Basic form unconditionally in Loaded.
//   - That makes EDIT look like ADD and can trigger re-inserts.
//
// Notes:
// - ELOG DPAPI is allowed elsewhere; this file stays out of crypto.
// - This file only inserts today; update/edit wiring can come later.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Security.Utility.Storage; // SecureEncryptedDataStore (SEDS)
using MWPV.Services;
using MWPV.View.UserControls.CategoryItems;

namespace MWPV.View.UserControls
{
    public partial class CategoryItemEditorTabs : UserControl
    {
        public event EventHandler? Submitted;
        public event EventHandler? Canceled;
        public event EventHandler<string>? PasswordValidationFailed;

        private int _categoryKey;
        private string _categoryName = string.Empty;

        private bool _isEditMode;

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

        private static bool IsEditModeFromSeds() => TryGetActiveCategoryItemId().HasValue;

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

            _isEditMode = IsEditModeFromSeds();

#if DEBUG
            var id = TryGetActiveCategoryItemId();
            Debug.WriteLine($"[ITEM-TABS] ConfigureForOpen: catKey={categoryKey}, name='{categoryName}', mode={(_isEditMode ? "EDIT" : "ADD")}, activeId={(id.HasValue ? id.Value : 0)}");
#endif

            InitializeUiForMode();
        }

        public void ConfigureForAdd(int categoryKey, string categoryName)
        {
            _categoryKey = categoryKey;
            _categoryName = categoryName;

            ClearSedsContext();
            _isEditMode = false;

#if DEBUG
            Debug.WriteLine($"[ITEM-TABS] ConfigureForAdd: catKey={categoryKey}, name='{categoryName}', mode=ADD (SEDS cleared)");
#endif
            InitializeUiForMode();
        }

        public void ConfigureForEdit(int categoryKey, string categoryName, object existingItem)
        {
            _categoryKey = categoryKey;
            _categoryName = categoryName;

            _isEditMode = IsEditModeFromSeds();

#if DEBUG
            Debug.WriteLine($"[ITEM-TABS] ConfigureForEdit: catKey={categoryKey}, name='{categoryName}', mode={(_isEditMode ? "EDIT" : "ADD")} (SEDS-derived)");
#endif
            InitializeUiForMode();

            // Future: load existing item into panels
        }

        private void InitializeUiForMode()
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

            if (!_isEditMode)
            {
                BasicPanel?.ClearForm();
                BasicPanel?.ResetUiState();
            }
            else
            {
                BasicPanel?.ResetUiState();
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

            _isEditMode = IsEditModeFromSeds();
            InitializeUiForMode();
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

        /* ======================= Persistence (INSERT only) ======================= */

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

            if (IsEditModeFromSeds())
                return true;

            try
            {
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
                        isBookmarkOnly: false
                    );
                }

                if (newId <= 0)
                {
                    SetStatus("Insert failed (no ItemId returned).");
                    return false;
                }

                if (newId > int.MaxValue)
                {
                    SetStatus("Insert failed (ItemId overflow).");
#if DEBUG
                    Debug.WriteLine($"[ITEM-TABS][INSERT] New ItemId={newId} exceeds Int32.MaxValue; cannot store in SEDS Int32.");
#endif
                    return false;
                }

                SetSedsContextForCategoryItem((int)newId);
                _isEditMode = true;

#if DEBUG
                Debug.WriteLine($"[ITEM-TABS][INSERT] New ItemId={newId} set into SEDS (kind={EntityKind_CategoryItem})");
#endif

                if (trigger == PersistTrigger.LeaveBasicTab)
                    SetStatus("");

                return true;
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine($"[ITEM-TABS][INSERT] FAILED: {ex}");
#endif
                SetStatus("Insert failed. See debug output.");
                return false;
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
                if (ItemTabs != null && ItemTabs.SelectedIndex != TabIndexBasic)
                    ItemTabs.SelectedIndex = TabIndexBasic;

                BasicPanel.FocusFirstError(okName, okPassword, okPin, okEmail, okPhone, isBookmarkOnly);
                SetStatus("Fix the highlighted errors before saving.");
                return;
            }

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
                if (ItemTabs != null)
                    ItemTabs.SelectedIndex = TabIndexBasic;

                BasicPanel.FocusFirstError(okName, okPassword, okPin, okEmail, okPhone, isBookmarkOnly);
                SetStatus("Bank Cards are staged, fix Basic tab errors before saving.");
                return;
            }

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

            if (!_isClosing && oldIndex == TabIndexBasic && newIndex != TabIndexBasic)
            {
                bool allowLeaveBasic = TryValidateAndPersistOnLeaveBasic();
                if (!allowLeaveBasic)
                {
                    _handlingTabSelection = true;
                    try { ItemTabs.SelectedIndex = TabIndexBasic; }
                    finally { _handlingTabSelection = false; }

                    _lastTabIndex = TabIndexBasic;
                    return;
                }
            }

            if (oldIndex == TabIndexBankCards && newIndex != TabIndexBankCards)
            {
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
                BasicPanel.FocusFirstError(okName, okPassword, okPin, okEmail, okPhone, isBookmarkOnly);
                SetStatus("Cannot leave Basic tab, fix highlighted errors first.");
                return false;
            }

            if (!TryPersistBasicIfNeeded(PersistTrigger.LeaveBasicTab, isBookmarkOnly))
                return false;

            return true;
        }

        /* ======================= Status line ======================= */

        private void SetStatus(string text)
        {
            if (txtStatusLine == null) return;
            txtStatusLine.Text = text ?? string.Empty;
        }
    }
}
