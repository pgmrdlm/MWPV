using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
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
        // Mode is now derived from SEDS via the ActiveEntityId entry (see helpers below).
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
         * Source of truth is the presence of a >0 numeric primary key in SEDS:
         *   - Missing / <=0 => Add
         *   - >0            => Edit
         */
        private const string SedsKey_ActiveEntityId = "MWPV.Context.ActiveEntityId";

        private static int? TryGetActiveEntityId()
        {
            try
            {
                if (!SecureEncryptedDataStore.HasKey(SedsKey_ActiveEntityId))
                    return null;

                var s = SecureEncryptedDataStore.GetString(SedsKey_ActiveEntityId);
                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) && id > 0)
                    return id;

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// True when SEDS contains a valid (>0) active entity id.
        /// </summary>
        private bool IsEditModeFromSeds() => TryGetActiveEntityId().HasValue;

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

            // Safety: if we’re unloaded without the host calling close-wipe,
            // we still wipe.
            try
            {
                BasicPanel?.WipeAllForHostClose();
                BankCardsPanel?.WipeAllForHostClose();
            }
            catch { }

            SetStatus("");
        }

        public void ConfigureForAdd(int categoryKey, string categoryName)
        {
            _categoryKey = categoryKey;
            _categoryName = categoryName;

#if DEBUG
            if (IsEditModeFromSeds())
                Debug.WriteLine("[ITEM-TABS][WARN] ConfigureForAdd called but SEDS ActiveEntityId is present. Caller should clear it BEFORE launching Add.");
#endif

            _isEditMode = false;

#if DEBUG
            Debug.WriteLine($"[ITEM-TABS] ConfigureForAdd: catKey={categoryKey}, name='{categoryName}', mode=ADD (SEDS-derived)");
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

            // If already edit-mode (SEDS has an id), do nothing for now (UPDATE later).
            if (IsEditModeFromSeds())
                return true;

            try
            {
                // Gather values from Basic panel
                string name = BasicPanel.GetItemNameTrim();
                string? desc = BasicPanel.GetDescriptionTrimOrNull();
                string? username = BasicPanel.GetUsernameTrimOrNull();
                string? url = BasicPanel.GetUrlTrimOrNull();
                string? email = BasicPanel.GetEmailTrimOrNull();
                string? phone = BasicPanel.GetPhoneTrimOrNull();

                // Secret blobs not wired yet (later: secure JSON)
                byte[]? secretMeta = null;
                byte[]? secretData = null;
                int? secretStorage = null;

                // PasswordHistory payload:
                // - Bookmark-only => empty blobs (temporary marker; later we can switch to "no history row")
                BasicPanel.BuildPasswordHistoryPayload(isBookmarkOnly, out byte[] pwCipher, out int? padLen, out byte[] pwSig);

                long newId = CategoryItemService.InsertCategoryItemWithPasswordHistory(
                    categoryKey: _categoryKey,
                    name: name,
                    description: desc,
                    username: username,
                    signInUrl: url,
                    accountEmail: email,
                    accountPhoneNumber: phone,
                    secretMeta: secretMeta,
                    secretData: secretData,
                    secretStorage: secretStorage,
                    isActive: 1,
                    pwCipher: pwCipher,
                    pwPadLen: padLen,
                    pwSig: pwSig
                );

                if (newId <= 0)
                {
                    SetStatus("Insert failed (no ItemId returned).");
                    return false;
                }

                // Single source of truth: set ActiveEntityId immediately to prevent double-insert.
                SecureEncryptedDataStore.SetString(SedsKey_ActiveEntityId, newId.ToString(CultureInfo.InvariantCulture));
                _isEditMode = true;

#if DEBUG
                Debug.WriteLine($"[ITEM-TABS][INSERT] New ItemId={newId} set into SEDS");
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
