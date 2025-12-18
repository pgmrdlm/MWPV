// CategoryItemBankCardsPanel.xaml.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MWPV.Services;
using MWPV.Utilities.Helpers;   // AutoHideTimer
using MWPV.Utilities.UI;        // UICleaner

namespace MWPV.View.UserControls.CategoryItems
{
    public partial class CategoryItemBankCardsPanel : UserControl
    {
        // ====================================================================
        // Events for the host (clean integration point)
        // ====================================================================

        /// <summary>
        /// Host should commit these rows into the parent editor model, then exit/close the editor.
        /// </summary>
        public event EventHandler<BankCardsCommitEventArgs>? SaveAndExitRequested;

        /// <summary>
        /// Host should exit/close the editor (no save).
        /// </summary>
        public event EventHandler? CancelAndExitRequested;

        // ====================================================================
        // Collections (binding / host access)
        // ====================================================================

        private readonly ObservableCollection<BankCardRow> _bankCardRows = new();
        private readonly ObservableCollection<CardTypeItem> _cardTypeItems = new();

        public ObservableCollection<BankCardRow> BankCardRows => _bankCardRows;
        public ObservableCollection<CardTypeItem> CardTypeItems => _cardTypeItems;

        private BankCardRow? _editingRow;

        // ====================================================================
        // Tab state (baseline snapshot + dirty/errors)
        // ====================================================================

        private readonly List<BankCardRow> _baselineRows = new();

        private bool _hasChanges;
        private bool _hasErrors;

        public bool HasChanges => _hasChanges;
        public bool HasErrors => _hasErrors;

        // Prevent dirty flag during programmatic loads/clears/restores.
        private bool _suppressDirty;

        // If we disable entry for the session (e.g., card-types load fails),
        // we keep Save disabled no matter what.
        private bool _entryDisabled;

        // ====================================================================
        // Reveal state + shared auto-hide timer
        // ====================================================================

        private bool _isCardNumberRevealed;
        private bool _isCvvRevealed;
        private bool _isPinRevealed;

        private readonly AutoHideTimer _revealAutoHide;

        // Prevent event stacking if control reloads
        private bool _uiEventsHooked;

        // Host-close guard:
        // We do NOT want to wipe grid rows during incidental unloads.
        private bool _hostRequestedCloseWipe;

        // Entry constraints
        private const int MaxCardNumberChars = 19; // digits + spaces

        public CategoryItemBankCardsPanel()
        {
            InitializeComponent();

            DataContext = this;

            Loaded += CategoryItemBankCardsPanel_Loaded;
            Unloaded += CategoryItemBankCardsPanel_Unloaded;

            _revealAutoHide = new AutoHideTimer(
                interval: TimeSpan.FromSeconds(20),
                onTimeout: OnRevealTimeout
            );

            _bankCardRows.CollectionChanged += BankCardRows_CollectionChanged;
        }

        // ====================================================================
        // HOST API (clean + explicit)
        // ====================================================================

        /// <summary>
        /// Host can call this to set initial rows (DB-loaded or parent-model-loaded).
        /// This becomes the baseline for Cancel.
        /// </summary>
        public void LoadFromHostRows(IEnumerable<BankCardRow>? rows)
        {
            _suppressDirty = true;
            try
            {
                HideAllRevealsAndStopTimer();
                WipeSensitiveEntryFields();
                ClearBankCardError();
                ResetBankCardFieldBackgrounds();

                DetachRowHandlers(_bankCardRows);
                _bankCardRows.Clear();

                if (rows != null)
                {
                    foreach (var r in rows)
                    {
                        var clone = CloneRow(r);
                        AttachRowHandlers(clone);
                        _bankCardRows.Add(clone);
                    }
                }

                CaptureBaselineFromCurrent();
                SetDirty(false);
                SetErrors(_entryDisabled);
                UpdateTabButtons();
            }
            finally
            {
                _suppressDirty = false;
            }
        }

        /// <summary>
        /// Call this from the host "X" (close editor) BEFORE removing the control.
        /// This is the strong wipe: entry + grid rows.
        /// </summary>
        public void WipeAllForHostClose()
        {
#if DEBUG
            Debug.WriteLine("[BANK-CARDS-PANEL][HOSTCLOSE] WipeAllForHostClose ENTER");
#endif
            _hostRequestedCloseWipe = true;

            HideAllRevealsAndStopTimer();
            WipeSensitiveEntryFields();
            WipeAndClearBankCardRows();

            ClearBankCardError();
            ResetBankCardFieldBackgrounds();

            _editingRow = null;
            SetDirty(false);
            SetErrors(false);
            UpdateTabButtons();

#if DEBUG
            Debug.WriteLine("[BANK-CARDS-PANEL][HOSTCLOSE] WipeAllForHostClose EXIT");
#endif
        }

        /// <summary>
        /// Future feature hook: "tabbing out" behavior.
        /// If changed and no errors, we commit + wipe entry + request exit.
        /// If changed and errors, deny navigate-away (returns false).
        /// If unchanged, allow navigate-away (returns true).
        /// </summary>
        public bool TryAutoCommitAndWipe()
        {
            if (!HasChanges)
                return true;

            // If the entry line is "half filled", we require Add/Update or Clear Row.
            if (EntryLineHasAnyInput())
            {
                ShowBankCardError("Finish Add/Update or Clear Row before leaving this tab.");
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

            RaiseSaveAndExit();
            return true;
        }

        // ====================================================================
        // Load / Unload
        // ====================================================================

        private void CategoryItemBankCardsPanel_Loaded(object? sender, RoutedEventArgs e)
        {
            HookUiEventsOnce();

            if (CardNumberPlainTextBox != null)
                CardNumberPlainTextBox.MaxLength = MaxCardNumberChars;

            LoadBankCardTypes();

            // If host didn't call LoadFromHostRows, baseline is whatever we have now.
            CaptureBaselineFromCurrent();
            SetDirty(false);
            SetErrors(_entryDisabled);
            UpdateTabButtons();
        }

        private void CategoryItemBankCardsPanel_Unloaded(object? sender, RoutedEventArgs e)
        {
            _revealAutoHide.Stop();
            UnhookUiEvents();

            // Always wipe entry line on unload.
            HideAllRevealsAndStopTimer();
            WipeSensitiveEntryFields();

            // Only wipe/clear the grid if host explicitly requested it.
            if (_hostRequestedCloseWipe)
                WipeAndClearBankCardRows();
        }

        // ====================================================================
        // Collection/Row change tracking (tab dirty state)
        // ====================================================================

        private void BankCardRows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (BankCardRow r in e.OldItems)
                    DetachRowHandlers(r);

            if (e.NewItems != null)
                foreach (BankCardRow r in e.NewItems)
                    AttachRowHandlers(r);

            MarkDirty();
        }

        private void AttachRowHandlers(BankCardRow row)
        {
            row.PropertyChanged -= Row_PropertyChanged;
            row.PropertyChanged += Row_PropertyChanged;
        }

        private void DetachRowHandlers(BankCardRow row)
        {
            row.PropertyChanged -= Row_PropertyChanged;
        }

        private void DetachRowHandlers(IEnumerable<BankCardRow> rows)
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
            // Save should only be enabled when:
            // - entry isn't disabled
            // - we have changes
            // - we have no errors
            // - the entry line is empty (we do not guess partial input)
            if (BtnTabSave != null)
                BtnTabSave.IsEnabled = !_entryDisabled && _hasChanges && !_hasErrors && !EntryLineHasAnyInput();

            // CANCEL MUST NEVER BE DISABLED.
            if (BtnTabCancel != null)
                BtnTabCancel.IsEnabled = true;
        }

        // ====================================================================
        // Baseline snapshot (for Cancel)
        // ====================================================================

        private void CaptureBaselineFromCurrent()
        {
            _baselineRows.Clear();
            foreach (var r in _bankCardRows)
                _baselineRows.Add(CloneRow(r));
        }

        private void RestoreBaseline()
        {
            _suppressDirty = true;
            try
            {
                HideAllRevealsAndStopTimer();
                WipeSensitiveEntryFields();
                ClearBankCardError();
                ResetBankCardFieldBackgrounds();

                DetachRowHandlers(_bankCardRows);

                // Wipe current rows before clearing
                foreach (var row in _bankCardRows.ToList())
                    WipeSingleRow(row);

                _bankCardRows.Clear();

                foreach (var baseRow in _baselineRows)
                {
                    var clone = CloneRow(baseRow);
                    AttachRowHandlers(clone);
                    _bankCardRows.Add(clone);
                }

                _editingRow = null;
                if (BtnBankCardAddOrUpdate != null)
                    BtnBankCardAddOrUpdate.Content = "Add";

                SetDirty(false);
                SetErrors(_entryDisabled);
                UpdateTabButtons();
            }
            finally
            {
                _suppressDirty = false;
            }
        }

        private static BankCardRow CloneRow(BankCardRow r)
        {
            return new BankCardRow
            {
                Id = r.Id,
                CardTypeId = r.CardTypeId,
                CardTypeDisplay = r.CardTypeDisplay ?? string.Empty,
                CardNumberRaw = r.CardNumberRaw ?? string.Empty,
                Expiration = r.Expiration ?? string.Empty,
                CvvRaw = r.CvvRaw ?? string.Empty,
                PinRaw = r.PinRaw ?? string.Empty,
                IsActive = r.IsActive
            };
        }

        // ====================================================================
        // Timer-driven reveal auto-hide
        // ====================================================================

        private void OnRevealTimeout()
        {
#if DEBUG
            Debug.WriteLine("[BANK-CARDS-PANEL] Reveal timer elapsed – hiding all reveals");
#endif
            HideAllRevealsAndStopTimer();
            ClearPlainRevealOverlays();
            UpdateTabButtons();
        }

        private void TouchRevealTimerIfNeeded()
        {
            bool anyRevealed = _isCardNumberRevealed || _isCvvRevealed || _isPinRevealed;
            _revealAutoHide.Touch(anyRevealed);
        }

        private void HideAllRevealsAndStopTimer()
        {
            _revealAutoHide.Stop();
            HideCardNumber();
            HideCvv();
            HidePin();
        }

        private void ClearPlainRevealOverlays()
        {
            if (CardNumberPlainTextBox != null) UICleaner.Clear(CardNumberPlainTextBox);
            if (CvvPlainTextBox != null) UICleaner.Clear(CvvPlainTextBox);
            if (PinPlainTextBox != null) UICleaner.Clear(PinPlainTextBox);
        }

        // ====================================================================
        // Load combos
        // ====================================================================

        private void LoadBankCardTypes()
        {
            try
            {
                const int comboTypeId = 2; // bank card types
                _cardTypeItems.Clear();

                var dbTypes = ComboDetailService.GetByTypeId(comboTypeId);

                foreach (var t in dbTypes.OrderBy(t => t.Seq))
                {
                    if (string.IsNullOrWhiteSpace(t.Code))
                        continue;

                    _cardTypeItems.Add(new CardTypeItem
                    {
                        ComboDetailId = t.ComboDet,
                        Code = t.Code,
                        Description = string.IsNullOrWhiteSpace(t.Description) ? t.Code : t.Description
                    });
                }

                if (CardTypeCombo != null)
                {
                    CardTypeCombo.IsEnabled = true;
                    if (_cardTypeItems.Count > 0 && CardTypeCombo.SelectedIndex < 0)
                        CardTypeCombo.SelectedIndex = 0;
                }

                _entryDisabled = false;
                EnableEntryControls(true);

                SetErrors(false);
                UpdateTabButtons();
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine($"[BANK-CARDS-PANEL][ERROR] LoadBankCardTypes failed: {ex}");
#endif
                _entryDisabled = true;

                ShowBankCardError("Unable to load card types. Bank card entry is disabled for this session.");

                EnableEntryControls(false);

                SetErrors(true);
                UpdateTabButtons();
            }
        }

        private void EnableEntryControls(bool enabled)
        {
            if (CardTypeCombo != null) CardTypeCombo.IsEnabled = enabled;
            if (CardNumberBox != null) CardNumberBox.IsEnabled = enabled;
            if (BtnViewCardNumber != null) BtnViewCardNumber.IsEnabled = enabled;

            if (ExpirationTextBox != null) ExpirationTextBox.IsEnabled = enabled;

            if (CvvBox != null) CvvBox.IsEnabled = enabled;
            if (BtnToggleCvvReveal != null) BtnToggleCvvReveal.IsEnabled = enabled;

            if (PinBox != null) PinBox.IsEnabled = enabled;
            if (BtnTogglePinReveal != null) BtnTogglePinReveal.IsEnabled = enabled;

            if (ChkCardActive != null) ChkCardActive.IsEnabled = enabled;

            if (BtnBankCardAddOrUpdate != null) BtnBankCardAddOrUpdate.IsEnabled = enabled;
            if (BtnBankCardClearRow != null) BtnBankCardClearRow.IsEnabled = enabled;
        }

        // ====================================================================
        // Entry line reveal helpers (read-only transparent overlay)
        // ====================================================================

        private void ShowCardNumber()
        {
            if (CardNumberBox == null || CardNumberPlainTextBox == null || BtnViewCardNumber == null)
                return;

            _isCardNumberRevealed = true;

            CardNumberPlainTextBox.Text = TrimToMaxChars(CardNumberBox.Password);
            CardNumberPlainTextBox.Visibility = Visibility.Visible;

            CardNumberBox.Visibility = Visibility.Collapsed;

            BtnViewCardNumber.ToolTip = "Hide card number";

            TouchRevealTimerIfNeeded();
        }

        private void HideCardNumber()
        {
            if (CardNumberBox == null || CardNumberPlainTextBox == null || BtnViewCardNumber == null)
                return;

            _isCardNumberRevealed = false;

            // Overlay is read-only, but we still re-sync for safety.
            CardNumberBox.Password = TrimToMaxChars(CardNumberPlainTextBox.Text);

            CardNumberBox.Visibility = Visibility.Visible;

            UICleaner.Clear(CardNumberPlainTextBox);
            CardNumberPlainTextBox.Visibility = Visibility.Collapsed;

            BtnViewCardNumber.ToolTip = "Show card number";
            TouchRevealTimerIfNeeded();
        }

        private void ShowCvv()
        {
            if (CvvBox == null || CvvPlainTextBox == null || BtnToggleCvvReveal == null)
                return;

            _isCvvRevealed = true;

            CvvPlainTextBox.Text = CvvBox.Password ?? string.Empty;
            CvvPlainTextBox.Visibility = Visibility.Visible;

            CvvBox.Visibility = Visibility.Collapsed;

            BtnToggleCvvReveal.ToolTip = "Hide CVV";
            TouchRevealTimerIfNeeded();
        }

        private void HideCvv()
        {
            if (CvvBox == null || CvvPlainTextBox == null || BtnToggleCvvReveal == null)
                return;

            _isCvvRevealed = false;

            CvvBox.Password = CvvPlainTextBox.Text ?? string.Empty;
            CvvBox.Visibility = Visibility.Visible;

            UICleaner.Clear(CvvPlainTextBox);
            CvvPlainTextBox.Visibility = Visibility.Collapsed;

            BtnToggleCvvReveal.ToolTip = "Show CVV";
            TouchRevealTimerIfNeeded();
        }

        private void ShowPin()
        {
            if (PinBox == null || PinPlainTextBox == null || BtnTogglePinReveal == null)
                return;

            _isPinRevealed = true;

            PinPlainTextBox.Text = PinBox.Password ?? string.Empty;
            PinPlainTextBox.Visibility = Visibility.Visible;

            PinBox.Visibility = Visibility.Collapsed;

            BtnTogglePinReveal.ToolTip = "Hide card PIN";
            TouchRevealTimerIfNeeded();
        }

        private void HidePin()
        {
            if (PinBox == null || PinPlainTextBox == null || BtnTogglePinReveal == null)
                return;

            _isPinRevealed = false;

            PinBox.Password = PinPlainTextBox.Text ?? string.Empty;
            PinBox.Visibility = Visibility.Visible;

            UICleaner.Clear(PinPlainTextBox);
            PinPlainTextBox.Visibility = Visibility.Collapsed;

            BtnTogglePinReveal.ToolTip = "Show card PIN";
            TouchRevealTimerIfNeeded();
        }

        // ====================================================================
        // Entry line change handlers
        // ====================================================================

        private void CardNumberBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (CardNumberBox == null)
                return;

            var trimmed = TrimToMaxChars(CardNumberBox.Password);
            if (!string.Equals(trimmed, CardNumberBox.Password, StringComparison.Ordinal))
                CardNumberBox.Password = trimmed;

            // Do NOT mirror plaintext while hidden; we only populate overlay on reveal.
            MarkDirty();
            TouchRevealTimerIfNeeded();
        }

        private void CvvBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            MarkDirty();
            TouchRevealTimerIfNeeded();
        }

        private void PinBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            MarkDirty();
            TouchRevealTimerIfNeeded();
        }

        // ====================================================================
        // Reveal button handlers
        // ====================================================================

        private void BtnViewCardNumber_Click(object sender, RoutedEventArgs e)
        {
            ClearBankCardError();
            ResetBankCardFieldBackgrounds();

            // If a row is selected, "peek" it into the entry line, reveal card number only.
            if (BankCardGrid != null && BankCardGrid.SelectedItem is BankCardRow row)
            {
                _suppressDirty = true;
                try
                {
                    _editingRow = null;
                    if (BtnBankCardAddOrUpdate != null)
                        BtnBankCardAddOrUpdate.Content = "Add";

                    var cardType = _cardTypeItems.FirstOrDefault(ct => ct.ComboDetailId == row.CardTypeId);
                    if (CardTypeCombo != null)
                    {
                        if (cardType != null)
                            CardTypeCombo.SelectedItem = cardType;
                        else
                            CardTypeCombo.SelectedIndex = _cardTypeItems.Count > 0 ? 0 : -1;
                    }

                    if (CardNumberBox != null)
                        CardNumberBox.Password = TrimToMaxChars(row.CardNumberRaw ?? string.Empty);

                    if (ExpirationTextBox != null)
                        ExpirationTextBox.Text = row.Expiration ?? string.Empty;

                    // Do NOT surface CVV or PIN for peek
                    if (CvvBox != null) UICleaner.Clear(CvvBox);
                    HideCvv();

                    if (PinBox != null) UICleaner.Clear(PinBox);
                    HidePin();

                    if (ChkCardActive != null)
                        ChkCardActive.IsChecked = row.IsActive;

                    ShowCardNumber();
                    CardNumberPlainTextBox?.Focus();
                }
                finally
                {
                    _suppressDirty = false;
                }

                UpdateTabButtons();
                return;
            }

            // Otherwise toggle the entry reveal only
            if (_isCardNumberRevealed)
            {
                HideCardNumber();
                CardNumberBox?.Focus();
            }
            else
            {
                ShowCardNumber();
                CardNumberPlainTextBox?.Focus();
            }
        }

        private void BtnToggleCvvReveal_Click(object sender, RoutedEventArgs e)
        {
            if (_isCvvRevealed) HideCvv();
            else ShowCvv();
        }

        private void BtnTogglePinReveal_Click(object sender, RoutedEventArgs e)
        {
            if (_isPinRevealed) HidePin();
            else ShowPin();
        }

        // ====================================================================
        // Row-level button handlers (Add/Update + Clear Row)
        // ====================================================================

        private void OnBankCardAddOrUpdateClick(object sender, RoutedEventArgs e)
        {
            ClearBankCardError();

            if (_entryDisabled)
            {
                ShowBankCardError("Bank card entry is disabled for this session.");
                SetErrors(true);
                UpdateTabButtons();
                return;
            }

            if (!ValidateBankCardFields(showErrors: true))
            {
                SetErrors(true);
                UpdateTabButtons();
                return;
            }

            var selection = CardTypeCombo?.SelectedItem as CardTypeItem;
            if (selection == null)
            {
                ShowBankCardError("Please choose a card type.", CardTypeCombo);
                SetErrors(true);
                UpdateTabButtons();
                return;
            }

            string cardNumber = GetCurrentCardNumber();
            string expirationInput = (ExpirationTextBox?.Text ?? "").Trim();

            string cvv = _isCvvRevealed ? (CvvPlainTextBox?.Text ?? string.Empty) : (CvvBox?.Password ?? string.Empty);
            string pin = _isPinRevealed ? (PinPlainTextBox?.Text ?? string.Empty) : (PinBox?.Password ?? string.Empty);

            bool isActive = ChkCardActive?.IsChecked == true;

            if (!TryValidateExpiration(expirationInput, out string expNormalized, out _))
            {
                ShowBankCardError("Expiration date is invalid.", ExpirationTextBox);
                SetErrors(true);
                UpdateTabButtons();
                return;
            }

            // Enforce one-per-type
            if (_editingRow == null)
            {
                bool duplicateType = _bankCardRows.Any(r => r.CardTypeId == selection.ComboDetailId);
                if (duplicateType)
                {
                    ShowBankCardError("Only one card of each type is allowed.", CardTypeCombo);
                    SetErrors(true);
                    UpdateTabButtons();
                    return;
                }

                var row = new BankCardRow
                {
                    Id = 0,
                    CardTypeId = selection.ComboDetailId,
                    CardTypeDisplay = selection.DisplayText,
                    CardNumberRaw = cardNumber,
                    Expiration = expNormalized,
                    CvvRaw = cvv,
                    PinRaw = pin,
                    IsActive = isActive
                };

                _bankCardRows.Add(row);
            }
            else
            {
                _editingRow.CardTypeId = selection.ComboDetailId;
                _editingRow.CardTypeDisplay = selection.DisplayText;
                _editingRow.CardNumberRaw = cardNumber;
                _editingRow.Expiration = expNormalized;
                _editingRow.CvvRaw = cvv;
                _editingRow.PinRaw = pin;
                _editingRow.IsActive = isActive;
            }

            ClearEntryFields();   // wipes entry line + clears edit mode
            SetErrors(false);
            MarkDirty();
            UpdateTabButtons();
        }

        private void OnBankCardClearRowClick(object sender, RoutedEventArgs e)
        {
            ClearBankCardError();
            ClearEntryFields();
            SetErrors(false);
            UpdateTabButtons();
        }

        private void OnBankCardFieldLostFocus(object sender, RoutedEventArgs e)
        {
            if (_entryDisabled)
            {
                SetErrors(true);
                UpdateTabButtons();
                return;
            }

            bool ok = ValidateBankCardFields(showErrors: true);
            SetErrors(!ok);
            UpdateTabButtons();
        }

        private void OnBankCardEditClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not BankCardRow row)
                return;

            _suppressDirty = true;
            try
            {
                _editingRow = row;

                var cardType = _cardTypeItems.FirstOrDefault(ct => ct.ComboDetailId == row.CardTypeId);
                if (CardTypeCombo != null && cardType != null)
                    CardTypeCombo.SelectedItem = cardType;

                if (CardNumberBox != null)
                    CardNumberBox.Password = TrimToMaxChars(row.CardNumberRaw);
                HideCardNumber();

                if (ExpirationTextBox != null)
                    ExpirationTextBox.Text = row.Expiration ?? string.Empty;

                if (CvvBox != null)
                    CvvBox.Password = row.CvvRaw ?? string.Empty;
                HideCvv();

                if (PinBox != null)
                    PinBox.Password = row.PinRaw ?? string.Empty;
                HidePin();

                if (ChkCardActive != null)
                    ChkCardActive.IsChecked = row.IsActive;

                if (BtnBankCardAddOrUpdate != null)
                    BtnBankCardAddOrUpdate.Content = "Update";

                ClearBankCardError();
                ResetBankCardFieldBackgrounds();
                SetErrors(false);
            }
            finally
            {
                _suppressDirty = false;
            }

            UpdateTabButtons();
        }

        private void OnBankCardDeleteClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not BankCardRow row)
                return;

            ClearBankCardError();

            // Keep original rule:
            if (row.Id != 0)
            {
                ShowBankCardError("Existing cards can't be deleted here. Edit the card or mark it inactive instead.");
                SetErrors(true);
                UpdateTabButtons();
                return;
            }

            if (_editingRow == row)
                ClearEntryFields();

            WipeSingleRow(row);
            _bankCardRows.Remove(row);

            SetErrors(false);
            MarkDirty();
            UpdateTabButtons();
        }

        // ====================================================================
        // TAB-LEVEL Save/Cancel handlers (key design)
        // ====================================================================

        private void OnTabSaveClick(object sender, RoutedEventArgs e)
        {
            ClearBankCardError();

            if (_entryDisabled)
            {
                ShowBankCardError("Bank card entry is disabled for this session.");
                SetErrors(true);
                UpdateTabButtons();
                return;
            }

            // If entry line has partial input, we do not guess.
            if (EntryLineHasAnyInput())
            {
                ShowBankCardError("Finish Add/Update or Clear Row before saving.");
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

            RaiseSaveAndExit();
        }

        private void OnTabCancelClick(object sender, RoutedEventArgs e)
        {
            // Cancel means: restore baseline rows, wipe entry line, hide reveals, clear errors, exit.
            RestoreBaseline();

            HideAllRevealsAndStopTimer();
            WipeSensitiveEntryFields();
            ClearBankCardError();
            ResetBankCardFieldBackgrounds();

            CancelAndExitRequested?.Invoke(this, EventArgs.Empty);
        }

        private void RaiseSaveAndExit()
        {
            var payload = _bankCardRows.Select(CloneRow).ToList();

            CaptureBaselineFromCurrent();
            SetDirty(false);
            SetErrors(_entryDisabled);

            // Wipe entry line & reveals so we leave this tab clean.
            HideAllRevealsAndStopTimer();
            WipeSensitiveEntryFields();
            ClearBankCardError();
            ResetBankCardFieldBackgrounds();

            UpdateTabButtons();

            SaveAndExitRequested?.Invoke(this, new BankCardsCommitEventArgs(payload));
        }

        // ====================================================================
        // Validation helpers
        // ====================================================================

        private bool ValidateTabStateOnly(bool showErrors)
        {
            if (EntryLineHasAnyInput())
            {
                if (showErrors) ShowBankCardError("Finish Add/Update or Clear Row before saving.");
                return false;
            }

            foreach (var row in _bankCardRows)
            {
                if (!TryValidateCardNumber(row.CardNumberRaw ?? string.Empty, out var cardErr))
                {
                    if (showErrors) ShowBankCardError($"Invalid card number in grid: {cardErr}");
                    return false;
                }

                if (!TryValidateExpiration(row.Expiration ?? string.Empty, out _, out var expErr))
                {
                    if (showErrors) ShowBankCardError($"Invalid expiration in grid: {expErr}");
                    return false;
                }

                if (!TryValidateCvv(row.CvvRaw ?? string.Empty, out var cvvErr))
                {
                    if (showErrors) ShowBankCardError($"Invalid CVV in grid: {cvvErr}");
                    return false;
                }

                if (!TryValidateCardPin(row.PinRaw ?? string.Empty, out var pinErr))
                {
                    if (showErrors) ShowBankCardError($"Invalid PIN in grid: {pinErr}");
                    return false;
                }
            }

            return true;
        }

        private bool ValidateBankCardFields(bool showErrors)
        {
            if (showErrors)
                ResetBankCardFieldBackgrounds();

            var selection = CardTypeCombo?.SelectedItem as CardTypeItem;
            string cardNumber = GetCurrentCardNumber();
            string expiration = (ExpirationTextBox?.Text ?? "").Trim();

            string cvv = _isCvvRevealed ? (CvvPlainTextBox?.Text ?? string.Empty) : (CvvBox?.Password ?? string.Empty);
            string pin = _isPinRevealed ? (PinPlainTextBox?.Text ?? string.Empty) : (PinBox?.Password ?? string.Empty);

            if (selection == null)
            {
                if (showErrors) ShowBankCardError("Please choose a card type.", CardTypeCombo);
                return false;
            }

            if (!TryValidateCardNumber(cardNumber, out string cardError))
            {
                if (showErrors) ShowBankCardError(cardError, _isCardNumberRevealed ? (Control?)CardNumberPlainTextBox : CardNumberBox);
                return false;
            }

            if (!TryValidateExpiration(expiration, out _, out string expError))
            {
                if (showErrors) ShowBankCardError(expError, ExpirationTextBox);
                return false;
            }

            if (!TryValidateCvv(cvv, out string cvvError))
            {
                if (showErrors) ShowBankCardError(cvvError, _isCvvRevealed ? (Control?)CvvPlainTextBox : CvvBox);
                return false;
            }

            if (!TryValidateCardPin(pin, out string pinError))
            {
                if (showErrors) ShowBankCardError(pinError, _isPinRevealed ? (Control?)PinPlainTextBox : PinBox);
                return false;
            }

            if (showErrors)
                ClearBankCardError();

            return true;
        }

        private static bool TryValidateCardNumber(string cardNumber, out string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(cardNumber))
            {
                errorMessage = "Card number is required.";
                return false;
            }

            var digitsAndSpaces = new string(cardNumber.Where(c => char.IsDigit(c) || c == ' ').ToArray());
            if (!string.Equals(cardNumber, digitsAndSpaces, StringComparison.Ordinal))
            {
                errorMessage = "Card number must contain digits and spaces only.";
                return false;
            }

            var onlyDigits = new string(cardNumber.Where(char.IsDigit).ToArray());
            if (onlyDigits.Length < 12 || onlyDigits.Length > 19)
            {
                errorMessage = "Card number must be between 12 and 19 digits.";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        private static bool TryValidateCvv(string cvv, out string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(cvv))
            {
                errorMessage = string.Empty; // optional
                return true;
            }

            if (!cvv.All(char.IsDigit) || cvv.Length < 3 || cvv.Length > 4)
            {
                errorMessage = "CVV must be 3–4 digits.";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        private static bool TryValidateCardPin(string pin, out string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(pin))
            {
                errorMessage = string.Empty; // optional
                return true;
            }

            if (!pin.All(char.IsDigit) || pin.Length < 4 || pin.Length > 6)
            {
                errorMessage = "PIN must be 4–6 digits.";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        private static bool TryValidateExpiration(string input, out string normalized, out string errorMessage)
        {
            normalized = string.Empty;
            errorMessage = string.Empty;

            input = (input ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(input))
            {
                errorMessage = "Expiration date is required.";
                return false;
            }

            string[] parts = input.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                errorMessage = "Expiration must be in MM/YY or MM/YYYY format.";
                return false;
            }

            if (!int.TryParse(parts[0], out int month) || month < 1 || month > 12)
            {
                errorMessage = "Expiration month must be between 01 and 12.";
                return false;
            }

            if (!int.TryParse(parts[1], out int year))
            {
                errorMessage = "Expiration year is invalid.";
                return false;
            }

            if (year < 100)
                year += 2000;

            int currentYear = DateTime.Today.Year;
            int maxYear = currentYear + 5;
            if (year > maxYear)
            {
                errorMessage = $"Expiration year cannot be more than 5 years from now ({currentYear}–{maxYear}).";
                return false;
            }

            var lastDayOfMonth = DateTime.DaysInMonth(year, month);
            var expirationDate = new DateTime(year, month, lastDayOfMonth);

            if (expirationDate < DateTime.Today)
            {
                errorMessage = "Expiration date must be this month or later.";
                return false;
            }

            normalized = $"{month:00}/{year}";
            return true;
        }

        private void ShowBankCardError(string message, Control? field = null)
        {
            if (BankCardErrorTextBlock != null)
                BankCardErrorTextBlock.Text = message ?? string.Empty;

            if (field != null)
                field.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x99, 0x99));
        }

        private void ClearBankCardError()
        {
            if (BankCardErrorTextBlock != null)
                BankCardErrorTextBlock.Text = string.Empty;

            ResetBankCardFieldBackgrounds();
        }

        private void ResetBankCardFieldBackgrounds()
        {
            CardTypeCombo?.ClearValue(BackgroundProperty);

            CardNumberBox?.ClearValue(BackgroundProperty);
            CardNumberPlainTextBox?.ClearValue(BackgroundProperty);

            ExpirationTextBox?.ClearValue(BackgroundProperty);

            CvvBox?.ClearValue(BackgroundProperty);
            CvvPlainTextBox?.ClearValue(BackgroundProperty);

            PinBox?.ClearValue(BackgroundProperty);
            PinPlainTextBox?.ClearValue(BackgroundProperty);
        }

        // ====================================================================
        // Entry field lifecycle / wipe
        // ====================================================================

        private void WipeSensitiveEntryFields()
        {
            HideAllRevealsAndStopTimer();

            if (CardNumberBox != null) UICleaner.Clear(CardNumberBox);
            if (CvvBox != null) UICleaner.Clear(CvvBox);
            if (PinBox != null) UICleaner.Clear(PinBox);

            ClearPlainRevealOverlays();

            if (ExpirationTextBox != null) UICleaner.Clear(ExpirationTextBox);
        }

        private void ClearEntryFields()
        {
            _suppressDirty = true;
            try
            {
                if (CardTypeCombo != null)
                    CardTypeCombo.SelectedIndex = _cardTypeItems.Count > 0 ? 0 : -1;

                if (CardNumberBox != null) UICleaner.Clear(CardNumberBox);
                HideCardNumber();

                if (ExpirationTextBox != null) UICleaner.Clear(ExpirationTextBox);

                if (CvvBox != null) UICleaner.Clear(CvvBox);
                HideCvv();

                if (PinBox != null) UICleaner.Clear(PinBox);
                HidePin();

                if (ChkCardActive != null)
                    ChkCardActive.IsChecked = true;

                _editingRow = null;
                if (BtnBankCardAddOrUpdate != null)
                    BtnBankCardAddOrUpdate.Content = "Add";

                ClearBankCardError();
                ResetBankCardFieldBackgrounds();
            }
            finally
            {
                _suppressDirty = false;
            }
        }

        private void WipeAndClearBankCardRows()
        {
            foreach (var row in _bankCardRows.ToList())
                WipeSingleRow(row);

            _bankCardRows.Clear();
        }

        private static void WipeSingleRow(BankCardRow row)
        {
            row.CardTypeDisplay = string.Empty;
            row.CardTypeId = 0;
            row.CardNumberRaw = string.Empty;
            row.Expiration = string.Empty;
            row.CvvRaw = string.Empty;
            row.PinRaw = string.Empty;
            row.IsActive = false;
        }

        // ====================================================================
        // Helpers
        // ====================================================================

        private bool EntryLineHasAnyInput()
        {
            // Card number
            if (CardNumberBox != null && !string.IsNullOrWhiteSpace(CardNumberBox.Password))
                return true;

            // Expiration
            if (ExpirationTextBox != null && !string.IsNullOrWhiteSpace(ExpirationTextBox.Text))
                return true;

            // CVV
            if (CvvBox != null && !string.IsNullOrWhiteSpace(CvvBox.Password))
                return true;

            // PIN
            if (PinBox != null && !string.IsNullOrWhiteSpace(PinBox.Password))
                return true;

            return false;
        }

        private string GetCurrentCardNumber()
        {
            if (CardNumberBox == null)
                return string.Empty;

            return TrimToMaxChars(CardNumberBox.Password).Trim();
        }

        private static string TrimToMaxChars(string? value)
        {
            var s = value ?? string.Empty;
            return s.Length <= MaxCardNumberChars ? s : s.Substring(0, MaxCardNumberChars);
        }

        // ====================================================================
        // UI event wiring (avoid duplicates on reload)
        // ====================================================================

        private void HookUiEventsOnce()
        {
            if (_uiEventsHooked)
                return;

            if (CardNumberBox != null)
            {
                CardNumberBox.PasswordChanged -= CardNumberBox_PasswordChanged;
                CardNumberBox.PasswordChanged += CardNumberBox_PasswordChanged;
            }

            if (CvvBox != null)
            {
                CvvBox.PasswordChanged -= CvvBox_PasswordChanged;
                CvvBox.PasswordChanged += CvvBox_PasswordChanged;
            }

            if (PinBox != null)
            {
                PinBox.PasswordChanged -= PinBox_PasswordChanged;
                PinBox.PasswordChanged += PinBox_PasswordChanged;
            }

            _uiEventsHooked = true;
        }

        private void UnhookUiEvents()
        {
            if (!_uiEventsHooked)
                return;

            if (CardNumberBox != null)
                CardNumberBox.PasswordChanged -= CardNumberBox_PasswordChanged;

            if (CvvBox != null)
                CvvBox.PasswordChanged -= CvvBox_PasswordChanged;

            if (PinBox != null)
                PinBox.PasswordChanged -= PinBox_PasswordChanged;

            _uiEventsHooked = false;
        }

        // ====================================================================
        // DTOs
        // ====================================================================

        public sealed class BankCardsCommitEventArgs : EventArgs
        {
            public BankCardsCommitEventArgs(IReadOnlyList<BankCardRow> rows) => Rows = rows;
            public IReadOnlyList<BankCardRow> Rows { get; }
        }

        public sealed class CardTypeItem
        {
            public int ComboDetailId { get; set; }
            public string Code { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;

            public string DisplayText => string.IsNullOrWhiteSpace(Description) ? Code : Description;
        }

        public sealed class BankCardRow : INotifyPropertyChanged
        {
            public int Id { get; set; }

            private int _cardTypeId;
            public int CardTypeId
            {
                get => _cardTypeId;
                set { if (_cardTypeId != value) { _cardTypeId = value; OnPropertyChanged(nameof(CardTypeId)); } }
            }

            private string _cardTypeDisplay = string.Empty;
            public string CardTypeDisplay
            {
                get => _cardTypeDisplay;
                set { if (_cardTypeDisplay != value) { _cardTypeDisplay = value ?? string.Empty; OnPropertyChanged(nameof(CardTypeDisplay)); } }
            }

            private string _cardNumberRaw = string.Empty;
            public string CardNumberRaw
            {
                get => _cardNumberRaw;
                set
                {
                    if (_cardNumberRaw != value)
                    {
                        _cardNumberRaw = value ?? string.Empty;
                        OnPropertyChanged(nameof(CardNumberRaw));
                        OnPropertyChanged(nameof(CardNumberMasked));
                    }
                }
            }

            public string CardNumberMasked
            {
                get
                {
                    if (string.IsNullOrWhiteSpace(_cardNumberRaw))
                        return string.Empty;

                    var trimmed = new string(_cardNumberRaw.Where(char.IsDigit).ToArray());
                    if (trimmed.Length <= 4)
                        return "•••• " + trimmed;

                    return "•••• " + trimmed[^4..];
                }
            }

            private string _expiration = string.Empty;
            public string Expiration
            {
                get => _expiration;
                set { if (_expiration != value) { _expiration = value ?? string.Empty; OnPropertyChanged(nameof(Expiration)); } }
            }

            private string _cvvRaw = string.Empty;
            public string CvvRaw
            {
                get => _cvvRaw;
                set
                {
                    if (_cvvRaw != value)
                    {
                        _cvvRaw = value ?? string.Empty;
                        OnPropertyChanged(nameof(CvvRaw));
                        OnPropertyChanged(nameof(CvvMasked));
                    }
                }
            }

            public string CvvMasked => string.IsNullOrEmpty(_cvvRaw) ? string.Empty : "•••";

            private string _pinRaw = string.Empty;
            public string PinRaw
            {
                get => _pinRaw;
                set
                {
                    if (_pinRaw != value)
                    {
                        _pinRaw = value ?? string.Empty;
                        OnPropertyChanged(nameof(PinRaw));
                        OnPropertyChanged(nameof(PinMasked));
                    }
                }
            }

            public string PinMasked => string.IsNullOrEmpty(_pinRaw) ? string.Empty : "•••";

            private bool _isActive = true;
            public bool IsActive
            {
                get => _isActive;
                set { if (_isActive != value) { _isActive = value; OnPropertyChanged(nameof(IsActive)); } }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged(string propertyName) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
