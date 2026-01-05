// File: View/UserControls/CategoryItemEditorTabs.xaml.cs

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
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

        // LEGACY: retained for compatibility / future cleanup.
        // Mode is derived from SEDS (kind+id).
        private bool _isEditMode;

        // Prevent event stacking
        private bool _panelsHooked;

        // Tab switching guard
        private bool _handlingTabSelection;
        private int _lastTabIndex;

        // Closing guard
        private bool _isClosing;

        // Draft rows (host can read later when persistence is wired)
        public IReadOnlyList<CategoryItemBankCardsPanel.BankCardRow> BankCardsDraftRows { get; private set; }
            = Array.Empty<CategoryItemBankCardsPanel.BankCardRow>();

        // Tab indexes
        private const int TabIndexBasic = 0;
        private const int TabIndexBankCards = 1;

        /*
         * ADD vs EDIT (SEDS is single source of truth)
         * -------------------------------------------
         * We only treat SEDS as "active CategoryItem edit" if:
         *   CurrentEntityKind == "CategoryItem" AND CurrentEntityId > 0
         *
         * This prevents stale/mismatched IDs coming from other flows (grid/panel/etc.).
         */
        private const string EntityKind_CategoryItem = "CategoryItem";
        private static readonly string SedsKey_EntityKind = SecureEncryptedDataStore.ContextKeys.CurrentEntityKind;
        private static readonly string SedsKey_EntityId = SecureEncryptedDataStore.ContextKeys.CurrentEntityId;

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
            // Write kind first so any reader won't treat an id as CategoryItem unless kind matches.
            SecureEncryptedDataStore.SetString(SedsKey_EntityKind, EntityKind_CategoryItem);
            SecureEncryptedDataStore.SetInt32(SedsKey_EntityId, id);
        }

        private static void ClearSedsContext()
        {
            try { SecureEncryptedDataStore.Clear(SedsKey_EntityId); } catch { }
            try { SecureEncryptedDataStore.Clear(SedsKey_EntityKind); } catch { }
        }

        public CategoryItemEditorTabs()
        {
            InitializeComponent();

            Loaded += CategoryItemEditorTabs_Loaded;
            Unloaded += CategoryItemEditorTabs_Unloaded;
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

            // Keep old behavior: clear + reset on load so we never reopen “dirty”.
            BasicPanel?.ClearForm();
            BasicPanel?.ResetUiState();

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

            // Safety: if we’re unloaded without the host calling close-wipe, still wipe.
            try
            {
                BasicPanel?.WipeAllForHostClose();
                BankCardsPanel?.WipeAllForHostClose();
            }
            catch { }

            // Safety: clear the context so we don’t reopen with a stale id.
            try { ClearSedsContext(); } catch { }

            SetStatus("");
        }

        public void ConfigureForAdd(int categoryKey, string categoryName)
        {
            _categoryKey = categoryKey;
            _categoryName = categoryName;

            // ADD means: we MUST NOT carry any stale “active id” forward.
            ClearSedsContext();
            _isEditMode = false;

#if DEBUG
            Debug.WriteLine($"[ITEM-TABS] ConfigureForAdd: catKey={categoryKey}, name='{categoryName}', mode=ADD (SEDS cleared)");
#endif

            BasicPanel?.ClearForm();
            BasicPanel?.ResetUiState();
            SetStatus("");
        }

        public void ConfigureForEdit(int categoryKey, string categoryName, object existingItem)
        {
            _categoryKey = categoryKey;
            _categoryName = categoryName;

            bool isEdit = IsEditModeFromSeds();
            _isEditMode = isEdit;

#if DEBUG
            Debug.WriteLine($"[ITEM-TABS] ConfigureForEdit: catKey={categoryKey}, name='{categoryName}', mode={(isEdit ? "EDIT" : "ADD")} (SEDS-derived)");
#endif

            // TODO: Map existingItem -> fields once persistence is wired.
            BasicPanel?.ResetUiState();
            SetStatus("");
        }

        /* ======================= Host API (wipe ordering) ======================= */

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
                // Always clear context on close to avoid stale edit-mode next time.
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

        /* ======================= Crypto bridge (masked fields -> BLOBs) ======================= */

        // IMPORTANT:
        // For v1 INSERT, we reuse DPAPI (same approach as BasicPanel.BuildPasswordHistoryPayload).
        // Later, we will swap this to vault-key encryption (from keyset.json / SEDS) in ONE place.

        private static readonly byte[] Entropy_Email = Encoding.UTF8.GetBytes("MWPV:CITabs:Email:v1");
        private static readonly byte[] Entropy_Phone = Encoding.UTF8.GetBytes("MWPV:CITabs:Phone:v1");
        private static readonly byte[] Entropy_Pin = Encoding.UTF8.GetBytes("MWPV:CITabs:Pin:v1");

        private static byte[]? EncryptMaskedOrNull_Email(string? value) => EncryptDpapiUtf8OrNull(value, Entropy_Email);
        private static byte[]? EncryptMaskedOrNull_Phone(string? value) => EncryptDpapiUtf8OrNull(value, Entropy_Phone);
        private static byte[]? EncryptMaskedOrNull_Pin(string? value) => EncryptDpapiUtf8OrNull(value, Entropy_Pin);

        private static byte[]? EncryptDpapiUtf8OrNull(string? value, byte[] entropy)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            byte[] plain = Encoding.UTF8.GetBytes(value.Trim());
            try
            {
                return ProtectedData.Protect(plain, entropy, DataProtectionScope.CurrentUser);
            }
            finally
            {
                Array.Clear(plain, 0, plain.Length);
            }
        }

        /* ======================= Persistence (INSERT only today) ======================= */

        private enum PersistTrigger
        {
            LeaveBasicTab,
            SaveAndExit
        }

        private bool TryPersistBasicIfNeeded(PersistTrigger trigger, bool isBookmarkOnly)
        {
            // Today: INSERT only. Edit/Update is later.
            if (_isClosing)
                return true;

            if (BasicPanel == null)
                return true;

            // If already edit-mode (SEDS has matching kind+id), do nothing for now (UPDATE later).
            if (IsEditModeFromSeds())
                return true;

            byte[]? emailCipher = null;
            byte[]? phoneCipher = null;
            byte[]? pinCipher = null;

            byte[]? pwCipher = null;
            byte[]? pwSig = null;
            int? padLen = null;

            try
            {
                // Gather values from Basic panel (plaintext fields)
                string name = BasicPanel.GetItemNameTrim();
                string? desc = BasicPanel.GetDescriptionTrimOrNull();
                string? username = BasicPanel.GetUsernameTrimOrNull();
                string? url = BasicPanel.GetUrlTrimOrNull();

                // Gather masked fields (we store encrypted BLOBs)
                string? emailPlain = BasicPanel.GetEmailTrimOrNull();
                string? phonePlain = BasicPanel.GetPhoneTrimOrNull();

                // PIN is currently optional; if you later add BasicPanel.GetPinTrimOrNull(), wire it here.
                string? pinPlain = null;

                emailCipher = EncryptMaskedOrNull_Email(emailPlain);
                phoneCipher = EncryptMaskedOrNull_Phone(phonePlain);
                pinCipher = EncryptMaskedOrNull_Pin(pinPlain);

                long newId;

                if (isBookmarkOnly)
                {
                    newId = CategoryItemService.InsertCategoryItemOnly(
                        categoryKey: _categoryKey,
                        name: name,
                        description: desc,
                        username: username,
                        signInUrl: url,
                        accountEmail: emailCipher,
                        accountPhoneNumber: phoneCipher,
                        pin: pinCipher,
                        isActive: 1
                    );
                }
                else
                {
                    BasicPanel.BuildPasswordHistoryPayload(isBookmarkOnly: false, out pwCipher!, out padLen, out pwSig!);

                    newId = CategoryItemService.InsertCategoryItemWithPasswordHistory(
                        categoryKey: _categoryKey,
                        name: name,
                        description: desc,
                        username: username,
                        signInUrl: url,
                        accountEmail: emailCipher,
                        accountPhoneNumber: phoneCipher,
                        isActive: 1,
                        pwCipher: pwCipher,
                        pwPadLen: padLen,
                        pwSig: pwSig,
                        pin: pinCipher
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

                // Single source of truth: set kind+id immediately to prevent double-insert.
                SetSedsContextForCategoryItem((int)newId);
                _isEditMode = true;

#if DEBUG
                Debug.WriteLine($"[ITEM-TABS][INSERT] New ItemId={newId} set into SEDS (kind={EntityKind_CategoryItem}, id={SedsKey_EntityId})");
#endif

                // For tab-leave, keep UI open; for SaveAndExit, caller will close.
                if (trigger == PersistTrigger.LeaveBasicTab)
                    SetStatus(""); // stay quiet on autosave

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
            finally
            {
                // Best-effort cleanup of local buffers (service should not retain these).
                try { if (emailCipher is { Length: > 0 }) Array.Clear(emailCipher, 0, emailCipher.Length); } catch { }
                try { if (phoneCipher is { Length: > 0 }) Array.Clear(phoneCipher, 0, phoneCipher.Length); } catch { }
                try { if (pinCipher is { Length: > 0 }) Array.Clear(pinCipher, 0, pinCipher.Length); } catch { }

                try { if (pwCipher is { Length: > 0 }) Array.Clear(pwCipher, 0, pwCipher.Length); } catch { }
                try { if (pwSig is { Length: > 0 }) Array.Clear(pwSig, 0, pwSig.Length); } catch { }
            }
        }

        /* ======================= Basic panel -> Save/Cancel ======================= */

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

            // INSERT (if needed) before closing
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

        /* ======================= Bank Cards panel integration ======================= */

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
                SetStatus("Bank Cards are staged — fix Basic tab errors before saving.");
                return;
            }

            // INSERT (if needed) before closing
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

            // Leaving BASIC: validate; if ok, auto-INSERT (if needed).
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

            // Leaving Bank Cards: enforce its own rule.
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
                SetStatus("Cannot leave Basic tab — fix highlighted errors first.");
                return false;
            }

            // Auto-INSERT if this is a new item.
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
