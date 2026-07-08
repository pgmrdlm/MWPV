// File: View/UserControls/CategoryItems/tmp_CategoryItemAccountsPanel.xaml.cs
//
// FULL REWRITE (match current XAML exactly)
//
// Notes:
// - XAML keeps only account type, account number, and active controls in this review copy.
// - Grid edit/delete are "selected row" buttons (no per-row Tag buttons).
// - Grid bindings expect: AccountTypeDisplay, AccountNumberMasked, IsActive.
// - This panel maintains its own UI row DTO (AccountRow) with raw + masked account-number fields.
// - Host can load rows from the current service row shape via LoadFromHostRows(...).
// - Save raises SaveAndExitRequested with payload rows (raw account number included).
// - No delete in service => existing rows (Id != 0) are not deletable here (same policy as before).

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MWPV.Services;
using MWPV.Utilities.Helpers;   // AutoHideTimer
using MWPV.Utilities.UI;        // UICleaner
using Security.Utility.Storage; // SecureEncryptedDataStore (SEDS)

namespace MWPV.View.UserControls.CategoryItems
{
    public partial class CategoryItemAccountsPanel : UserControl
    {
        // ============================================================
        // Events for host
        // ============================================================

        public event EventHandler<AccountsCommitEventArgs>? SaveAndExitRequested;
        public event EventHandler? CancelAndExitRequested;

        // ============================================================
        // Collections (binding)
        // ============================================================

        private readonly ObservableCollection<AccountRow> _accountRows = new();
        private readonly ObservableCollection<AccountTypeItem> _accountTypeItems = new();

        public ObservableCollection<AccountRow> AccountRows => _accountRows;
        public ObservableCollection<AccountTypeItem> AccountTypeItems => _accountTypeItems;

        private AccountRow? _editingRow;

        // ============================================================
        // Tab state
        // ============================================================

        private readonly List<AccountRow> _baselineRows = new();

        private bool _hasChanges;
        private bool _hasErrors;
        private bool _newAccountSessionStarted;

        public bool HasChanges => _hasChanges;
        public bool HasErrors => _hasErrors;

        private bool _suppressDirty;
        private bool _entryDisabled;

        // ============================================================
        // Reveal state + timer (read-only overlays)
        // ============================================================

        private bool _isAccountNumberRevealed;

        private readonly AutoHideTimer _revealAutoHide;
        private bool _uiEventsHooked;

        // Host-close guard
        private bool _hostRequestedCloseWipe;

        private const int MaxAccountNumberChars = 19; // digits + spaces
        private const string FreeformAccountTypeCode = "FREEFORM";

        private const string SedsKey_AccountSelectedRowId = "BC.Selected.CardId";
        private const string SedsKey_AccountSelectedNumber = "BC.Selected.Number";

        private bool _isSelectedProtectedViewActive;
        // ============================================================
        // Ctor
        // ============================================================

        public CategoryItemAccountsPanel()
        {
            InitializeComponent();

            DataContext = this;

            Loaded += CategoryItemAccountsPanel_Loaded;
            Unloaded += CategoryItemAccountsPanel_Unloaded;

            _revealAutoHide = new AutoHideTimer(
                interval: TimeSpan.FromSeconds(20),
                onTimeout: OnRevealTimeout);

            _accountRows.CollectionChanged += AccountRows_CollectionChanged;
        }

        // ============================================================
        // HOST API
        // ============================================================

        /// <summary>
        /// Host loads rows into this panel from the Accounts service row shape.
        /// </summary>
        public void LoadFromHostRows(IEnumerable<tmp_CategoryItemAccountsService.AccountListRow>? rows)
        {
            _suppressDirty = true;
            try
            {
                ClearNewAccountSessionTracking("LoadFromHostRows");
                HideRevealsAndStopTimer(clearRevealOverlays: true);
                WipeSensitiveEntryFields();
                ClearAccountError();
                ResetAccountFieldBackgrounds();
                ClearSelectedAccountDetailSedsBestEffort();
                _isSelectedProtectedViewActive = false;

                if (AccountGrid != null)
                    AccountGrid.SelectedItem = null;

                DetachRowHandlers(_accountRows);
                WipeAndClearAccountRows();

                if (rows != null)
                {
                    foreach (var r in rows)
                    {
                        string accountTypeFreeform = string.IsNullOrWhiteSpace(r.AccountTypeFreeform)
                            ? string.Empty
                            : r.AccountTypeFreeform.Trim();
                        var ui = new AccountRow
                        {
                            Id = r.Id,
                            AccountTypeId = r.AccountTypeId,
                            AccountTypeDisplay = r.AccountTypeDisplay ?? string.Empty,
                            AccountTypeFreeform = accountTypeFreeform,

                            // Service never returns plaintext. Keep raw empty.
                            AccountNumberRaw = string.Empty,

                            IsActive = r.IsActive,

                            // For display
                            AccountNumberMasked = r.AccountNumberMasked ?? string.Empty
                        };

                        AttachRowHandlers(ui);
                        _accountRows.Add(ui);
                    }
                }

                CaptureBaselineFromCurrent();
                SetDirty(false);
                SetErrors(_entryDisabled);
                SetEditingMode(null);
                UpdateTabButtons();
            }
            finally
            {
                _suppressDirty = false;
            }
        }

        /// <summary>
        /// Call from host close path BEFORE removing the control.
        /// Strong wipe: entry + grid rows.
        /// </summary>
        public void WipeAllForHostClose()
        {
            _hostRequestedCloseWipe = true;
            ClearNewAccountSessionTracking("WipeAllForHostClose");

            HideRevealsAndStopTimer(clearRevealOverlays: true);
            WipeSensitiveEntryFields();
            WipeAndClearAccountRows();
            ClearSelectedAccountDetailSedsBestEffort();
            _isSelectedProtectedViewActive = false;

            ClearAccountError();
            ResetAccountFieldBackgrounds();

            _editingRow = null;
            SetDirty(false);
            SetErrors(false);
            UpdateTabButtons();
        }

        /// <summary>
        /// TAB SWITCH behavior ONLY:
        /// - If entry line has any input: block leave.
        /// - If grid invalid: block leave.
        /// - Otherwise: clear entry line and allow leave.
        /// </summary>
        public bool TryAutoCommitAndWipe()
        {
            if (EntryLineHasAnyInput())
            {
                ShowAccountError("Finish Add/Update or Clear Row before leaving this tab.");
                SetErrors(true);
                UpdateTabButtons();
                return false;
            }

            if (!ValidateTabStateOnly(showErrors: true))
            {
                SetErrors(true);
                UpdateTabButtons();
                return false;
            }

            ClearAccountError();
            ClearNewAccountSessionTracking("TryAutoCommitAndWipe");
            ClearEntryFields();
            ClearSelectedAccountDetailSedsBestEffort();
            _isSelectedProtectedViewActive = false;
            SetErrors(false);
            UpdateTabButtons();
            return true;
        }

        /// <summary>
        /// HOST-CLOSE behavior ONLY:
        /// Returns true when Accounts currently has meaningful session work that should
        /// drive an explicit host-close decision before the window is allowed to close.
        /// </summary>
        public bool HasHostCloseSessionWork()
        {
            bool hasWork =
                _newAccountSessionStarted ||
                _hasChanges ||
                EntryLineHasAnyInput() ||
                _editingRow != null;


            return hasWork;
        }

        /// <summary>
        /// HOST-CLOSE behavior ONLY:
        /// Validates whether a save can proceed right now and, on success, returns the payload
        /// the host coordinator should persist using the existing save pipeline.
        /// </summary>
        public bool TryBuildHostCloseSavePayload(out IReadOnlyList<AccountRow> rows)
        {
            rows = Array.Empty<AccountRow>();

            ClearAccountError();

            if (_entryDisabled)
            {
                ShowAccountError("Account entry is disabled for this session.");
                SetErrors(true);
                UpdateTabButtons();
                return false;
            }

            if (EntryLineHasAnyInput())
            {
                ShowAccountError("Finish Add/Update or Clear Row before saving.");
                SetErrors(true);
                UpdateTabButtons();
                return false;
            }

            if (HasPendingBlankAddAttempt())
            {
                ShowAccountError("Finish Add/Update or Clear Row before saving.");
                SetErrors(true);
                UpdateTabButtons();
                return false;
            }

            if (!ValidateTabStateOnly(showErrors: true))
            {
                SetErrors(true);
                UpdateTabButtons();
                return false;
            }

            ClearAccountError();
            SetErrors(false);
            UpdateTabButtons();

            rows = _accountRows.Select(CloneRow).ToList();

            return true;
        }

        /// <summary>
        /// HOST-CLOSE behavior ONLY:
        /// Best-effort discard preparation before the real host-close wipe path runs.
        /// This intentionally does not perform the final wipe; host-close cleanup still owns that.
        /// </summary>
        public bool TryPrepareHostCloseDiscard()
        {
            try
            {
                ClearNewAccountSessionTracking("TryPrepareHostCloseDiscard");
                ClearAccountError();
                SetErrors(false);
                UpdateTabButtons();

                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        // ============================================================
        // Load / Unload
        // ============================================================

        private void CategoryItemAccountsPanel_Loaded(object? sender, RoutedEventArgs e)
        {
            HookUiEventsOnce();

            if (AccountNumberBox != null)
                AccountNumberBox.MaxLength = MaxAccountNumberChars;

            // REVIEW-ONLY:
            // Use the existing numeric ComboTypeId active-row loader for Accounts.
            LoadAccountTypes();

            CaptureBaselineFromCurrent();
            SetDirty(false);
            SetErrors(_entryDisabled);
            UpdateTabButtons();
        }

        private void CategoryItemAccountsPanel_Unloaded(object? sender, RoutedEventArgs e)
        {
            _revealAutoHide.Stop();
            UnhookUiEvents();

            HideRevealsAndStopTimer(clearRevealOverlays: true);
            WipeSensitiveEntryFields();
            ClearSelectedAccountDetailSedsBestEffort();
            _isSelectedProtectedViewActive = false;

            if (_hostRequestedCloseWipe)
                WipeAndClearAccountRows();
        }

        // ============================================================
        // Dirty tracking
        // ============================================================

        private void AccountRows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (AccountRow r in e.OldItems)
                    DetachRowHandlers(r);

            if (e.NewItems != null)
                foreach (AccountRow r in e.NewItems)
                    AttachRowHandlers(r);

            MarkDirty();
        }

        private void AttachRowHandlers(AccountRow row)
        {
            row.PropertyChanged -= Row_PropertyChanged;
            row.PropertyChanged += Row_PropertyChanged;
        }

        private void DetachRowHandlers(AccountRow row)
        {
            row.PropertyChanged -= Row_PropertyChanged;
        }

        private void DetachRowHandlers(IEnumerable<AccountRow> rows)
        {
            foreach (var r in rows)
                DetachRowHandlers(r);
        }

        private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            MarkDirty();
        }

        private void MarkDirty()
        {
            if (_suppressDirty)
                return;

            if (!_hasChanges)
                _hasChanges = true;

            UpdateTabButtons();
        }

        private void SetDirty(bool value) => _hasChanges = value;
        private void SetErrors(bool value) => _hasErrors = value;

        private void UpdateTabButtons()
        {
            ApplyProtectedViewControlState();

            if (BtnTabSave != null)
            {
                BtnTabSave.IsEnabled =
                    !_entryDisabled &&
                    _hasChanges &&
                    !_hasErrors &&
                    !EntryLineHasAnyInput();
            }

            if (BtnTabCancel != null)
                BtnTabCancel.IsEnabled = true;
        }

        // ============================================================
        // Baseline snapshot (Cancel behavior)
        // ============================================================

        private void CaptureBaselineFromCurrent()
        {
            _baselineRows.Clear();
            foreach (var r in _accountRows)
                _baselineRows.Add(CloneRow(r));
        }

        private void RestoreBaseline()
        {
            _suppressDirty = true;
            try
            {
                ClearNewAccountSessionTracking("RestoreBaseline");
                HideRevealsAndStopTimer(clearRevealOverlays: true);
                WipeSensitiveEntryFields();
                ClearAccountError();
                ResetAccountFieldBackgrounds();

                DetachRowHandlers(_accountRows);
                WipeAndClearAccountRows();

                foreach (var baseRow in _baselineRows)
                {
                    var clone = CloneRow(baseRow);
                    AttachRowHandlers(clone);
                    _accountRows.Add(clone);
                }

                SetEditingMode(null);

                SetDirty(false);
                SetErrors(_entryDisabled);
                UpdateTabButtons();
            }
            finally
            {
                _suppressDirty = false;
            }
        }

        private static AccountRow CloneRow(AccountRow r)
        {
            return new AccountRow
            {
                Id = r.Id,
                AccountTypeId = r.AccountTypeId,
                AccountTypeDisplay = r.AccountTypeDisplay ?? string.Empty,
                AccountTypeFreeform = r.AccountTypeFreeform ?? string.Empty,
                AccountNumberRaw = r.AccountNumberRaw ?? string.Empty,
                IsActive = r.IsActive,
                AccountNumberMasked = r.AccountNumberMasked ?? string.Empty
            };
        }

        // ============================================================
        // Reveal timer
        // ============================================================

        private void OnRevealTimeout()
        {
            HideRevealsAndStopTimer(clearRevealOverlays: true);
            UpdateTabButtons();
        }

        private void TouchRevealTimerIfNeeded()
        {
            _revealAutoHide.Touch(_isAccountNumberRevealed);
        }

        private void HideRevealsAndStopTimer(bool clearRevealOverlays)
        {
            _revealAutoHide.Stop();
            HideAccountNumber(clearOverlay: clearRevealOverlays);
        }

        private static void ClearRevealOverlayTextOnly(TextBox? tb)
        {
            if (tb == null) return;
            tb.Text = string.Empty;
        }

        // ============================================================
        // Combos
        // ============================================================

        private void LoadAccountTypes()
        {
            try
            {
                const int comboTypeId = 1; // current DB: account_types
                _accountTypeItems.Clear();

                var dbTypes = ComboDetailService.GetByTypeId(comboTypeId);

                foreach (var t in dbTypes.OrderBy(t => t.Seq))
                {
                    if (string.IsNullOrWhiteSpace(t.Code))
                        continue;

                    _accountTypeItems.Add(new AccountTypeItem
                    {
                        ComboDetailId = t.ComboDet,
                        Code = t.Code,
                        Description = string.IsNullOrWhiteSpace(t.Description) ? t.Code : t.Description
                    });
                }

                if (AccountTypeCombo != null)
                {
                    AccountTypeCombo.IsEnabled = true;
                    if (_accountTypeItems.Count > 0 && AccountTypeCombo.SelectedIndex < 0)
                        AccountTypeCombo.SelectedIndex = 0;
                }

                _entryDisabled = false;
                EnableEntryControls(true);
                UpdateCustomAccountTypeUi(clearWhenHidden: false);
                SetErrors(false);
                UpdateTabButtons();
            }
            catch (Exception ex)
            {
                _entryDisabled = true;
                ShowAccountError("Unable to load account types. Account entry is disabled for this session.");
                EnableEntryControls(false);
                SetErrors(true);
                UpdateTabButtons();
            }
        }

        private void EnableEntryControls(bool enabled)
        {
            if (AccountTypeCombo != null) AccountTypeCombo.IsEnabled = enabled;
            if (AccountNumberBox != null) AccountNumberBox.IsEnabled = enabled;
            if (BtnViewAccountNumber != null) BtnViewAccountNumber.IsEnabled = enabled;
            if (ChkAccountActive != null) ChkAccountActive.IsEnabled = enabled;
            if (BtnAccountAddOrUpdate != null) BtnAccountAddOrUpdate.IsEnabled = enabled;
            if (BtnAccountClearRow != null) BtnAccountClearRow.IsEnabled = enabled;

            UpdateCustomAccountTypeUi(clearWhenHidden: false);
        }

        private void ApplyProtectedViewControlState()
        {
            bool editingExistingPersistedRow = _editingRow != null && _editingRow.Id > 0;
            bool allowExistingRowActiveToggle = !_entryDisabled && editingExistingPersistedRow;
            bool editable = !_entryDisabled && !_isSelectedProtectedViewActive && !editingExistingPersistedRow;

            if (AccountTypeCombo != null) AccountTypeCombo.IsEnabled = editable;
            if (AccountNumberBox != null) AccountNumberBox.IsEnabled = editable;
            if (ChkAccountActive != null) ChkAccountActive.IsEnabled = editable || allowExistingRowActiveToggle;

            bool allowReveal = !_entryDisabled;
            if (BtnViewAccountNumber != null) BtnViewAccountNumber.IsEnabled = allowReveal;

            bool allowCopy = !_entryDisabled && _isSelectedProtectedViewActive;
            if (BtnCopyAccountNumber != null)
            {
                BtnCopyAccountNumber.Visibility = allowCopy ? Visibility.Visible : Visibility.Collapsed;
                BtnCopyAccountNumber.IsEnabled = allowCopy;
            }

            bool hasSelectedExistingRow = AccountGrid?.SelectedItem is AccountRow selected && selected.Id > 0;
            bool showEditSelected = !_entryDisabled && _editingRow == null && _isSelectedProtectedViewActive && hasSelectedExistingRow;
            if (BtnAccountEditSelected != null)
            {
                BtnAccountEditSelected.Visibility = showEditSelected ? Visibility.Visible : Visibility.Collapsed;
                BtnAccountEditSelected.IsEnabled = showEditSelected;
            }

            if (BtnAccountAddOrUpdate != null)
                BtnAccountAddOrUpdate.IsEnabled = editable || allowExistingRowActiveToggle;

            if (BtnAccountClearRow != null)
                BtnAccountClearRow.IsEnabled = editable;

            UpdateCustomAccountTypeUi(clearWhenHidden: false);
        }

        private static bool IsFreeformAccountTypeCode(string? code)
        {
            return string.Equals((code ?? string.Empty).Trim(), FreeformAccountTypeCode, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsFreeformAccountType(AccountTypeItem? item)
        {
            return item != null && IsFreeformAccountTypeCode(item.Code);
        }

        private bool IsFreeformAccountTypeId(int accountTypeId)
        {
            return IsFreeformAccountType(_accountTypeItems.FirstOrDefault(ct => ct.ComboDetailId == accountTypeId));
        }

        private static string ResolveAccountTypeDisplay(AccountTypeItem selection, string? accountTypeFreeform)
        {
            if (IsFreeformAccountType(selection) && !string.IsNullOrWhiteSpace(accountTypeFreeform))
                return accountTypeFreeform.Trim();

            return selection.DisplayText;
        }

        private string GetCurrentCustomAccountType()
        {
            return (CustomAccountTypeTextBox?.Text ?? string.Empty).Trim();
        }

        private void UpdateCustomAccountTypeUi(bool clearWhenHidden)
        {
            bool isFreeformSelection = IsFreeformAccountType(AccountTypeCombo?.SelectedItem as AccountTypeItem);
            bool editingExistingPersistedRow = _editingRow != null && _editingRow.Id > 0;
            bool showCustomAccountType =
                isFreeformSelection &&
                !_isSelectedProtectedViewActive &&
                !editingExistingPersistedRow;

            if (CustomAccountTypePanel != null)
                CustomAccountTypePanel.Visibility = showCustomAccountType ? Visibility.Visible : Visibility.Collapsed;

            if (CustomAccountTypeTextBox != null)
            {
                CustomAccountTypeTextBox.IsEnabled = showCustomAccountType && !_entryDisabled;

                if (!showCustomAccountType && clearWhenHidden)
                    UICleaner.Clear(CustomAccountTypeTextBox);
            }
        }

        private void AccountTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool isFreeformSelection = IsFreeformAccountType(AccountTypeCombo?.SelectedItem as AccountTypeItem);
            UpdateCustomAccountTypeUi(clearWhenHidden: !isFreeformSelection);

            if (_suppressDirty)
                return;

            if (_entryDisabled)
            {
                SetErrors(true);
                UpdateTabButtons();
                return;
            }

            MarkDirty();
            UpdateTabButtons();
        }

        // ============================================================
        // Reveal helpers (read-only overlays; no editing in overlay)
        // ============================================================

        private void ShowAccountNumber()
        {
            if (AccountNumberBox == null || AccountNumberPlainTextBox == null || BtnViewAccountNumber == null)
                return;

            _isAccountNumberRevealed = true;

            string trimmed = TrimToMaxChars(AccountNumberBox.Password);
            if (!string.Equals(trimmed, AccountNumberBox.Password, StringComparison.Ordinal))
                AccountNumberBox.Password = trimmed;

            MaskedRevealOverlayHelper.ShowPlainOverlay(AccountNumberBox, AccountNumberPlainTextBox, trimmed);
            BtnViewAccountNumber.ToolTip = "Hide account number";
            TouchRevealTimerIfNeeded();
        }

        private void HideAccountNumber(bool clearOverlay)
        {
            if (AccountNumberBox == null || AccountNumberPlainTextBox == null || BtnViewAccountNumber == null)
                return;

            if (!_isAccountNumberRevealed && AccountNumberBox.Visibility == Visibility.Visible)
                return;

            _isAccountNumberRevealed = false;
            AccountNumberBox.Visibility = Visibility.Visible;

            if (clearOverlay)
                ClearRevealOverlayTextOnly(AccountNumberPlainTextBox);

            MaskedRevealOverlayHelper.RestoreMaskedOverlay(AccountNumberBox, AccountNumberPlainTextBox);
            BtnViewAccountNumber.ToolTip = "Show account number";
            TouchRevealTimerIfNeeded();
        }

        // ============================================================
        // Entry change handlers (dirty + timer)
        // ============================================================

        private void AccountNumberBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (AccountNumberBox == null)
                return;

            string trimmed = TrimToMaxChars(AccountNumberBox.Password);
            if (!string.Equals(trimmed, AccountNumberBox.Password, StringComparison.Ordinal))
                AccountNumberBox.Password = trimmed;

            if (_isAccountNumberRevealed && AccountNumberPlainTextBox != null)
                AccountNumberPlainTextBox.Text = trimmed;

            MarkDirty();
            TouchRevealTimerIfNeeded();
        }

        // ============================================================
        // Reveal button handlers
        // ============================================================

        private void BtnViewAccountNumber_Click(object sender, RoutedEventArgs e)
        {
            ClearAccountError();
            ResetAccountFieldBackgrounds();

            if (_isAccountNumberRevealed) HideAccountNumber(clearOverlay: true);
            else ShowAccountNumber();
        }

        // ============================================================
        // Protected-view copy buttons
        // ============================================================

        private void BtnCopyAccountNumber_Click(object sender, RoutedEventArgs e)
        {
            CopySelectedProtectedFieldToClipboard(SedsKey_AccountSelectedNumber, "No account number is available to copy.");
        }

        private void CopySelectedProtectedFieldToClipboard(string sedsKey, string emptyMessage)
        {
            if (!_isSelectedProtectedViewActive || _entryDisabled)
                return;

            string value = ReadSelectedProtectedFieldFromSeds(sedsKey);
            if (string.IsNullOrWhiteSpace(value))
            {
                ShowAccountError(emptyMessage);
                return;
            }

            if (ClipboardHelper.TryCopySensitiveText(value, out _, tag: "ACCOUNT.NUMBER"))
                ClearAccountError();
            else
                ShowAccountError("Unable to copy value to clipboard.");
        }
        // ============================================================
        // Row-level: Add/Update + Clear
        // ============================================================

        private void OnAccountAddOrUpdateClick(object sender, RoutedEventArgs e)
        {
            _isSelectedProtectedViewActive = false;
            ClearAccountError();

            bool isTrueAddMode = _editingRow == null;
            bool isUpdate = _editingRow != null;
            bool isExistingPersistedUpdate = _editingRow != null && _editingRow.Id > 0;

            if (isTrueAddMode)
            {
                BeginAddNewAccountSession();
            }

            if (_entryDisabled)
            {
                ShowAccountError("Account entry is disabled for this session.");
                SetErrors(true);
                UpdateTabButtons();
                return;
            }

            if (!ValidateAccountFields(showErrors: true))
            {
                SetErrors(true);
                UpdateTabButtons();
                return;
            }

            var selection = AccountTypeCombo?.SelectedItem as AccountTypeItem;
            if (selection == null)
            {
                ShowAccountError("Please choose an account type.", AccountTypeCombo);
                SetErrors(true);
                UpdateTabButtons();
                return;
            }

            string accountNumber = GetCurrentAccountNumber();
            string customAccountType = IsFreeformAccountType(selection) ? GetCurrentCustomAccountType() : string.Empty;
            bool isActive = ChkAccountActive?.IsChecked == true;

            bool selectedIsPrimary =
                string.Equals(selection.Code, "PRIMARY", StringComparison.OrdinalIgnoreCase);

            bool duplicateActivePrimary =
                selectedIsPrimary &&
                isActive &&
                _accountRows.Any(r =>
                    r.IsActive &&
                    r.AccountTypeId == selection.ComboDetailId &&
                    !ReferenceEquals(r, _editingRow));

            if (duplicateActivePrimary)
            {
                ShowAccountError("Only one active Primary account is allowed.", AccountTypeCombo);
                SetErrors(true);
                UpdateTabButtons();
                return;
            }

            string masked = MaskPanLast4(new string(accountNumber.Where(char.IsDigit).ToArray()));

            if (_editingRow == null)
            {
                var row = new AccountRow
                {
                    Id = 0,
                    AccountTypeId = selection.ComboDetailId,
                    AccountTypeDisplay = ResolveAccountTypeDisplay(selection, customAccountType),
                    AccountTypeFreeform = customAccountType,
                    AccountNumberRaw = accountNumber,
                    AccountNumberMasked = masked,
                    IsActive = isActive
                };

                _accountRows.Add(row);
            }
            else
            {
                if (isExistingPersistedUpdate)
                {
                    string preservedAccountNumber = _editingRow.AccountNumberRaw ?? string.Empty;
                    string preservedMasked = MaskPanLast4(new string(preservedAccountNumber.Where(char.IsDigit).ToArray()));

                    _editingRow.AccountNumberRaw = preservedAccountNumber;
                    _editingRow.AccountNumberMasked = preservedMasked;
                    _editingRow.IsActive = isActive;
                }
                else
                {
                    _editingRow.AccountTypeId = selection.ComboDetailId;
                    _editingRow.AccountTypeDisplay = ResolveAccountTypeDisplay(selection, customAccountType);
                    _editingRow.AccountTypeFreeform = customAccountType;
                    _editingRow.AccountNumberRaw = accountNumber;
                    _editingRow.AccountNumberMasked = masked;
                    _editingRow.IsActive = isActive;
                }
            }

            if (isUpdate)
                TeardownAfterSuccessfulUpdate();
            else
                ClearEntryFields();

            SetErrors(false);
            MarkDirty();
            UpdateTabButtons();

            if (isExistingPersistedUpdate || isTrueAddMode)
                RaiseSaveAndExitRequestOnly();
        }

        private void OnAccountClearRowClick(object sender, RoutedEventArgs e)
        {
            ClearNewAccountSessionTracking("OnAccountClearRowClick");
            _isSelectedProtectedViewActive = false;
            ClearAccountError();
            ClearEntryFields();
            SetErrors(false);
            UpdateTabButtons();
        }

        private void OnAccountFieldLostFocus(object sender, RoutedEventArgs e)
        {
            if (_entryDisabled)
            {
                SetErrors(true);
                UpdateTabButtons();
                return;
            }

            if (!EntryLineHasAnyInput())
            {
                ClearAccountError();
                SetErrors(false);
                UpdateTabButtons();
                return;
            }

            bool ok = ValidateAccountFields(showErrors: true);
            SetErrors(!ok);
            UpdateTabButtons();
        }

        private void AccountGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressDirty)
                return;

            if (AccountGrid?.SelectedItem is not AccountRow selected || selected.Id <= 0)
            {
                ClearSelectedAccountDetailSedsBestEffort();
                _isSelectedProtectedViewActive = false;
                UpdateTabButtons();
                return;
            }

            int? activeItemId = TryGetActiveCategoryItemIdFromSeds();
            if (!activeItemId.HasValue || activeItemId.Value <= 0)
            {
                ClearSelectedAccountDetailSedsBestEffort();
                _isSelectedProtectedViewActive = false;
                UpdateTabButtons();
                return;
            }

            string? accountNumberForEdit = tmp_CategoryItemAccountsService.LoadAccountNumberPlainByItemIdAndAccountId(
                activeItemId.Value,
                selected.Id);

            if (string.IsNullOrWhiteSpace(accountNumberForEdit))
            {
                ClearSelectedAccountDetailSedsBestEffort();
                _isSelectedProtectedViewActive = false;
                UpdateTabButtons();
                return;
            }

            StoreSelectedAccountDetailSedsBestEffort(selected.Id, accountNumberForEdit);
            PopulateProtectedViewFromSelectedDetail(selected, accountNumberForEdit);
        }

        private void PopulateProtectedViewFromSelectedDetail(AccountRow row, string accountNumber)
        {
            _suppressDirty = true;
            try
            {
                _isSelectedProtectedViewActive = true;
                SetEditingMode(null);

                var accountType = _accountTypeItems.FirstOrDefault(ct => ct.ComboDetailId == row.AccountTypeId);
                if (AccountTypeCombo != null)
                {
                    if (accountType != null) AccountTypeCombo.SelectedItem = accountType;
                    else AccountTypeCombo.SelectedIndex = _accountTypeItems.Count > 0 ? 0 : -1;
                }

                if (CustomAccountTypeTextBox != null)
                    CustomAccountTypeTextBox.Text = IsFreeformAccountTypeId(row.AccountTypeId) ? (row.AccountTypeFreeform ?? string.Empty) : string.Empty;

                if (AccountNumberBox != null)
                    AccountNumberBox.Password = TrimToMaxChars(accountNumber ?? string.Empty);
                HideAccountNumber(clearOverlay: true);

                if (ChkAccountActive != null)
                    ChkAccountActive.IsChecked = row.IsActive;

                ClearAccountError();
                ResetAccountFieldBackgrounds();
                SetErrors(false);
            }
            finally
            {
                _suppressDirty = false;
            }

            UpdateTabButtons();
        }

        private static int? TryGetActiveCategoryItemIdFromSeds()
        {
            return CategoryItemSedsContextHelper.TryGetCurrentCategoryItemId();
        }

        private static void ClearSelectedAccountDetailSedsBestEffort()
        {
            try { SecureEncryptedDataStore.Clear(SedsKey_AccountSelectedNumber); } catch { }
            try { SecureEncryptedDataStore.Clear(SedsKey_AccountSelectedRowId); } catch { }
        }

        private static void StoreSelectedAccountDetailSedsBestEffort(long rowId, string accountNumber)
        {
            ClearSelectedAccountDetailSedsBestEffort();

            try { SecureEncryptedDataStore.SetString(SedsKey_AccountSelectedRowId, rowId.ToString(CultureInfo.InvariantCulture)); } catch { }
            try { SecureEncryptedDataStore.SetString(SedsKey_AccountSelectedNumber, accountNumber ?? string.Empty); } catch { }
        }

        // ============================================================
        // Protected selected-row SEDS read helper
        // ============================================================
        private static string ReadSelectedProtectedFieldFromSeds(string sedsKey)
        {
            try
            {
                if (!SecureEncryptedDataStore.TryGetBytes(sedsKey, out var valueBytes) || valueBytes.Length == 0)
                    return string.Empty;

                try
                {
                    return Encoding.UTF8.GetString(valueBytes);
                }
                finally
                {
                    Array.Clear(valueBytes, 0, valueBytes.Length);
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool TryReadSelectedProtectedAccountNumberForEdit(long expectedRowId, out string accountNumber)
        {
            accountNumber = string.Empty;

            string selectedRowIdRaw = ReadSelectedProtectedFieldFromSeds(SedsKey_AccountSelectedRowId);
            if (!long.TryParse(selectedRowIdRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long selectedRowId))
                return false;

            if (selectedRowId != expectedRowId)
                return false;

            accountNumber = ReadSelectedProtectedFieldFromSeds(SedsKey_AccountSelectedNumber);
            return !string.IsNullOrWhiteSpace(accountNumber);
        }

        // ============================================================
        // Grid strip button: Edit Selected
        // ============================================================

        private bool TryPopulateEditorForEdit(AccountRow row)
        {
            string accountNumberForEdit = row.AccountNumberRaw ?? string.Empty;

            if (row.Id > 0)
            {
                if (!TryReadSelectedProtectedAccountNumberForEdit(row.Id, out accountNumberForEdit))
                {
                    ShowAccountError("Unable to open edit mode. Reselect the row and try again.");
                    SetErrors(true);
                    UpdateTabButtons();
                    return false;
                }
            }

            _isSelectedProtectedViewActive = false;

            _suppressDirty = true;
            try
            {
                SetEditingMode(row);

                var accountType = _accountTypeItems.FirstOrDefault(ct => ct.ComboDetailId == row.AccountTypeId);
                if (AccountTypeCombo != null)
                {
                    if (accountType != null) AccountTypeCombo.SelectedItem = accountType;
                    else AccountTypeCombo.SelectedIndex = _accountTypeItems.Count > 0 ? 0 : -1;
                }

                if (CustomAccountTypeTextBox != null)
                    CustomAccountTypeTextBox.Text = IsFreeformAccountTypeId(row.AccountTypeId) ? (row.AccountTypeFreeform ?? string.Empty) : string.Empty;

                if (AccountNumberBox != null)
                    AccountNumberBox.Password = TrimToMaxChars(accountNumberForEdit);
                row.AccountNumberRaw = TrimToMaxChars(accountNumberForEdit);
                HideAccountNumber(clearOverlay: true);

                if (ChkAccountActive != null)
                    ChkAccountActive.IsChecked = row.IsActive;

                ClearAccountError();
                ResetAccountFieldBackgrounds();
                SetErrors(false);
            }
            finally
            {
                _suppressDirty = false;
            }

            UpdateTabButtons();
            return true;
        }

        private void OnAccountEditClick(object sender, RoutedEventArgs e)
        {
            if (AccountGrid?.SelectedItem is not AccountRow row)
                return;
            TryPopulateEditorForEdit(row);
        }

        // ============================================================
        // Tab-level Save/Cancel
        // ============================================================

        private void OnTabSaveClick(object sender, RoutedEventArgs e)
        {
            ClearAccountError();

            if (_entryDisabled)
            {
                ShowAccountError("Account entry is disabled for this session.");
                SetErrors(true);
                UpdateTabButtons();
                return;
            }

            if (EntryLineHasAnyInput())
            {
                ShowAccountError("Finish Add/Update or Clear Row before saving.");
                SetErrors(true);
                UpdateTabButtons();
                return;
            }

            if (!ValidateTabStateOnly(showErrors: true))
            {
                SetErrors(true);
                UpdateTabButtons();
                return;
            }

            RaiseSaveAndExitRequestOnly();
        }

        private void OnTabCancelClick(object sender, RoutedEventArgs e)
        {
            ClearNewAccountSessionTracking("OnTabCancelClick");
            ClearAccountError();

            HideRevealsAndStopTimer(clearRevealOverlays: true);
            WipeSensitiveEntryFields();
            ResetAccountFieldBackgrounds();
            ClearSelectedAccountDetailSedsBestEffort();
            _isSelectedProtectedViewActive = false;

            CancelAndExitRequested?.Invoke(this, EventArgs.Empty);
        }

        private void RaiseSaveAndExitRequestOnly()
        {
            var payload = _accountRows.Select(CloneRow).ToList();
            SaveAndExitRequested?.Invoke(this, new AccountsCommitEventArgs(payload));
        }

        // ============================================================
        // Validation
        // ============================================================

        private bool ValidateTabStateOnly(bool showErrors)
        {
            if (EntryLineHasAnyInput())
            {
                if (showErrors) ShowAccountError("Finish Add/Update or Clear Row before saving.");
                return false;
            }

            foreach (var row in _accountRows)
            {
                if (row.Id == 0)
                {
                    if (!TryValidateAccountNumber(row.AccountNumberRaw ?? string.Empty, out var accountError))
                    {
                        if (showErrors) ShowAccountError($"Invalid account number in grid: {accountError}");
                        return false;
                    }
                }
            }

            return true;
        }

        private bool ValidateAccountFields(bool showErrors)
        {
            if (showErrors)
                ResetAccountFieldBackgrounds();

            var selection = AccountTypeCombo?.SelectedItem as AccountTypeItem;
            string accountNumber = GetCurrentAccountNumber();
            string customAccountType = GetCurrentCustomAccountType();

            if (selection == null)
            {
                if (showErrors) ShowAccountError("Please choose an account type.", AccountTypeCombo);
                return false;
            }

            if (IsFreeformAccountType(selection) && string.IsNullOrWhiteSpace(customAccountType))
            {
                if (showErrors) ShowAccountError("Custom account type is required for FREEFORM.", CustomAccountTypeTextBox);
                return false;
            }

            if (!TryValidateAccountNumber(accountNumber, out string accountError))
            {
                if (showErrors) ShowAccountError(accountError, AccountNumberBox);
                return false;
            }

            if (showErrors)
                ClearAccountError();

            return true;
        }

        private static bool TryValidateAccountNumber(string accountNumber, out string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(accountNumber))
            {
                errorMessage = "Account number is required.";
                return false;
            }

            var digitsAndSpaces = new string(accountNumber.Where(c => char.IsDigit(c) || c == ' ').ToArray());
            if (!string.Equals(accountNumber, digitsAndSpaces, StringComparison.Ordinal))
            {
                errorMessage = "Account number must contain digits and spaces only.";
                return false;
            }

            var onlyDigits = new string(accountNumber.Where(char.IsDigit).ToArray());
            if (onlyDigits.Length < 4 || onlyDigits.Length > 19)
            {
                errorMessage = "Account number must be between 4 and 19 digits.";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        private void ShowAccountError(string message, Control? field = null)
        {
            if (AccountErrorTextBlock != null)
                AccountErrorTextBlock.Text = message ?? string.Empty;

            if (field != null)
                field.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x99, 0x99));
        }

        private void ClearAccountError()
        {
            if (AccountErrorTextBlock != null)
                AccountErrorTextBlock.Text = string.Empty;

            ResetAccountFieldBackgrounds();
        }

        private void ResetAccountFieldBackgrounds()
        {
            AccountTypeCombo?.ClearValue(BackgroundProperty);
            CustomAccountTypeTextBox?.ClearValue(BackgroundProperty);
            AccountNumberBox?.ClearValue(BackgroundProperty);
            AccountNumberPlainTextBox?.ClearValue(BackgroundProperty);
        }

        // ============================================================
        // Entry lifecycle / wipe
        // ============================================================

        private void WipeSensitiveEntryFields()
        {
            HideRevealsAndStopTimer(clearRevealOverlays: true);

            if (CustomAccountTypeTextBox != null) UICleaner.Clear(CustomAccountTypeTextBox);
            if (AccountNumberBox != null) UICleaner.Clear(AccountNumberBox);
            ClearRevealOverlayTextOnly(AccountNumberPlainTextBox);
        }

        private void ClearEntryFields()
        {
            _suppressDirty = true;
            try
            {
                if (AccountTypeCombo != null)
                    AccountTypeCombo.SelectedIndex = _accountTypeItems.Count > 0 ? 0 : -1;

                if (CustomAccountTypeTextBox != null) UICleaner.Clear(CustomAccountTypeTextBox);
                if (AccountNumberBox != null) UICleaner.Clear(AccountNumberBox);
                HideAccountNumber(clearOverlay: true);

                if (ChkAccountActive != null)
                    ChkAccountActive.IsChecked = true;

                _isSelectedProtectedViewActive = false;
                SetEditingMode(null);

                ClearAccountError();
                ResetAccountFieldBackgrounds();
            }
            finally
            {
                _suppressDirty = false;
            }
        }

        private void TeardownAfterSuccessfulUpdate()
        {
            ClearEntryFields();
            ClearSelectedAccountDetailSedsBestEffort();
            _isSelectedProtectedViewActive = false;
            if (AccountGrid != null)
                AccountGrid.SelectedItem = null;
            SetEditingMode(null);
        }

        private void WipeAndClearAccountRows()
        {
            foreach (var row in _accountRows.ToList())
                row.Wipe();

            _accountRows.Clear();
        }

        // ============================================================
        // Helpers
        // ============================================================

        private void SetEditingMode(AccountRow? row)
        {
            _editingRow = row;

            if (BtnAccountAddOrUpdate != null)
                BtnAccountAddOrUpdate.Content = (_editingRow == null) ? "Add" : "Update";
        }

        private bool EntryLineHasAnyInput()
        {
            if (_isSelectedProtectedViewActive)
                return false;

            return (CustomAccountTypeTextBox != null && !string.IsNullOrWhiteSpace(CustomAccountTypeTextBox.Text)) ||
                   (AccountNumberBox != null && !string.IsNullOrWhiteSpace(AccountNumberBox.Password));
        }

        private string GetCurrentAccountNumber()
        {
            if (AccountNumberBox == null)
                return string.Empty;

            return TrimToMaxChars(AccountNumberBox.Password).Trim();
        }

        public void BeginAddNewAccountSession()
        {
            if (_entryDisabled || _isSelectedProtectedViewActive || _editingRow != null)
                return;

            if (_newAccountSessionStarted)
                return;

            _newAccountSessionStarted = true;

        }

        private bool HasPendingBlankAddAttempt()
        {
            return _newAccountSessionStarted &&
                   !_hasChanges &&
                   !EntryLineHasAnyInput() &&
                   _editingRow == null;
        }

        private void ClearNewAccountSessionTracking(string source)
        {
            if (!_newAccountSessionStarted)
                return;

            _newAccountSessionStarted = false;

        }

        private static string TrimToMaxChars(string? value)
        {
            var s = value ?? string.Empty;
            return s.Length <= MaxAccountNumberChars ? s : s.Substring(0, MaxAccountNumberChars);
        }

        private static string MaskPanLast4(string digits)
        {
            if (string.IsNullOrWhiteSpace(digits))
                return string.Empty;

            var d = digits.Trim();
            if (d.Length <= 4)
                return $"**** {d}";

            string last4 = d.Substring(d.Length - 4, 4);
            return $"**** {last4}";
        }

        // ============================================================
        // Wire/unwire events
        // ============================================================

        private void HookUiEventsOnce()
        {
            if (_uiEventsHooked)
                return;

            if (AccountNumberBox != null)
            {
                AccountNumberBox.PasswordChanged -= AccountNumberBox_PasswordChanged;
                AccountNumberBox.PasswordChanged += AccountNumberBox_PasswordChanged;
            }

            _uiEventsHooked = true;
        }

        private void UnhookUiEvents()
        {
            if (!_uiEventsHooked)
                return;

            if (AccountNumberBox != null)
                AccountNumberBox.PasswordChanged -= AccountNumberBox_PasswordChanged;

            _uiEventsHooked = false;
        }

        // ============================================================
        // DTOs
        // ============================================================

        public sealed class AccountsCommitEventArgs : EventArgs
        {
            public AccountsCommitEventArgs(IReadOnlyList<AccountRow> rows) => Rows = rows;
            public IReadOnlyList<AccountRow> Rows { get; }
        }

        public sealed class AccountTypeItem
        {
            public int ComboDetailId { get; set; }
            public string Code { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;

            public string DisplayText => string.IsNullOrWhiteSpace(Description) ? Code : Description;
        }

        public sealed class AccountRow : INotifyPropertyChanged
        {
            public long Id { get; set; }

            private int _accountTypeId;
            public int AccountTypeId
            {
                get => _accountTypeId;
                set { if (_accountTypeId != value) { _accountTypeId = value; OnPropertyChanged(nameof(AccountTypeId)); } }
            }

            private string _accountTypeDisplay = string.Empty;
            public string AccountTypeDisplay
            {
                get => _accountTypeDisplay;
                set { if (_accountTypeDisplay != value) { _accountTypeDisplay = value ?? string.Empty; OnPropertyChanged(nameof(AccountTypeDisplay)); } }
            }

            private string _accountTypeFreeform = string.Empty;
            public string AccountTypeFreeform
            {
                get => _accountTypeFreeform;
                set { if (_accountTypeFreeform != value) { _accountTypeFreeform = value ?? string.Empty; OnPropertyChanged(nameof(AccountTypeFreeform)); } }
            }

            private string _accountNumberRaw = string.Empty;
            public string AccountNumberRaw
            {
                get => _accountNumberRaw;
                set
                {
                    if (_accountNumberRaw != value)
                    {
                        _accountNumberRaw = value ?? string.Empty;
                        OnPropertyChanged(nameof(AccountNumberRaw));
                    }
                }
            }

            private bool _isActive = true;
            public bool IsActive
            {
                get => _isActive;
                set { if (_isActive != value) { _isActive = value; OnPropertyChanged(nameof(IsActive)); } }
            }

            private string _accountNumberMasked = string.Empty;
            public string AccountNumberMasked
            {
                get => _accountNumberMasked;
                set { if (_accountNumberMasked != value) { _accountNumberMasked = value ?? string.Empty; OnPropertyChanged(nameof(AccountNumberMasked)); } }
            }

            public void Wipe()
            {
                AccountTypeDisplay = string.Empty;
                AccountTypeId = 0;
                AccountTypeFreeform = string.Empty;
                AccountNumberRaw = string.Empty;
                AccountNumberMasked = string.Empty;
                IsActive = false;
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged(string propertyName) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
