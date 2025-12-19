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

// Adjust this namespace to match where you put ISensitiveWipe + SensitiveCollectionWiper
using Security.Utility.Wiping;

namespace MWPV.View.UserControls.CategoryItems
{
    public partial class CategoryItemBankCardsPanel : UserControl
    {
        // ====================================================================
        // Events for the host (clean integration point)
        // ====================================================================

        public event EventHandler<BankCardsCommitEventArgs>? SaveAndExitRequested;
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

        private bool _suppressDirty;
        private bool _entryDisabled;

        // ====================================================================
        // Reveal state + shared auto-hide timer
        // ====================================================================

        private bool _isCardNumberRevealed;
        private bool _isCvvRevealed;
        private bool _isPinRevealed;

        private readonly AutoHideTimer _revealAutoHide;

        private bool _uiEventsHooked;

        // Host-close guard: only the host close path may wipe the grid rows.
        private bool _hostRequestedCloseWipe;

        private const int MaxCardNumberChars = 19; // digits + spaces

        // Prevent recursive TextChanged work when we normalize input.
        private bool _suppressPlainTextNormalize;

        // GC is NOT a security guarantee; keep off unless we explicitly want it.
        private const bool ForceGcAfterHostCloseWipe = false;

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

        public void LoadFromHostRows(IEnumerable<BankCardRow>? rows)
        {
            _suppressDirty = true;
            try
            {
                HideRevealsAndStopTimer(copyBack: false, clearRevealOverlays: true);
                WipeSensitiveEntryFields();
                ClearBankCardError();
                ResetBankCardFieldBackgrounds();

                DetachRowHandlers(_bankCardRows);

                // Strong wipe previous grid contents before replacing
                WipeAndClearBankCardRows();

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
        /// Call this from the host close path BEFORE removing the control.
        /// Strong wipe: entry + grid rows.
        /// </summary>
        public void WipeAllForHostClose()
        {
#if DEBUG
            Debug.WriteLine("[BANK-CARDS-PANEL][HOSTCLOSE] WipeAllForHostClose ENTER");
#endif
            _hostRequestedCloseWipe = true;

            HideRevealsAndStopTimer(copyBack: false, clearRevealOverlays: true);
            WipeSensitiveEntryFields();
            WipeAndClearBankCardRows();

            ClearBankCardError();
            ResetBankCardFieldBackgrounds();

            _editingRow = null;
            SetDirty(false);
            SetErrors(false);
            UpdateTabButtons();

            if (ForceGcAfterHostCloseWipe)
                ForceGcBestEffort();

#if DEBUG
            Debug.WriteLine("[BANK-CARDS-PANEL][HOSTCLOSE] WipeAllForHostClose EXIT");
#endif
        }

        /// <summary>
        /// TAB SWITCH behavior ONLY (do not close editor):
        /// - If partial entry line: block leaving
        /// - If grid invalid: block leaving
        /// - If ok: wipe/clear ENTRY LINE ONLY and allow leaving
        /// </summary>
        public bool TryAutoCommitAndWipe()
        {
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

            ClearBankCardError();
            ClearEntryFields();
            SetErrors(false);
            UpdateTabButtons();

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

            CaptureBaselineFromCurrent();
            SetDirty(false);
            SetErrors(_entryDisabled);
            UpdateTabButtons();
        }

        private void CategoryItemBankCardsPanel_Unloaded(object? sender, RoutedEventArgs e)
        {
            _revealAutoHide.Stop();
            UnhookUiEvents();

            HideRevealsAndStopTimer(copyBack: false, clearRevealOverlays: true);
            WipeSensitiveEntryFields();

            // Only wipe/clear grid if host explicitly requested it.
            if (_hostRequestedCloseWipe)
                WipeAndClearBankCardRows();

            if (_hostRequestedCloseWipe && ForceGcAfterHostCloseWipe)
                ForceGcBestEffort();
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

        // ====================================================================
        // Baseline snapshot
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
                HideRevealsAndStopTimer(copyBack: false, clearRevealOverlays: true);
                WipeSensitiveEntryFields();
                ClearBankCardError();
                ResetBankCardFieldBackgrounds();

                DetachRowHandlers(_bankCardRows);

                WipeAndClearBankCardRows();

                foreach (var baseRow in _baselineRows)
                {
                    var clone = CloneRow(baseRow);
                    AttachRowHandlers(clone);
                    _bankCardRows.Add(clone);
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
        // Timer-driven reveal auto-hide (HIDE ONLY)
        // ====================================================================

        private void OnRevealTimeout()
        {
#if DEBUG
            Debug.WriteLine("[BANK-CARDS-PANEL] Reveal timer elapsed – hiding reveals (no wipe)");
#endif
            HideRevealsAndStopTimer(copyBack: true, clearRevealOverlays: true);
            UpdateTabButtons();
        }

        private void TouchRevealTimerIfNeeded()
        {
            bool anyRevealed = _isCardNumberRevealed || _isCvvRevealed || _isPinRevealed;
            _revealAutoHide.Touch(anyRevealed);
        }

        private void HideRevealsAndStopTimer(bool copyBack, bool clearRevealOverlays)
        {
            _revealAutoHide.Stop();

            HideCardNumber(copyBack: copyBack, clearOverlay: clearRevealOverlays);
            HideCvv(copyBack: copyBack, clearOverlay: clearRevealOverlays);
            HidePin(copyBack: copyBack, clearOverlay: clearRevealOverlays);
        }

        private static void ClearRevealOverlayTextOnly(TextBox? tb)
        {
            if (tb == null) return;
            tb.Text = string.Empty;
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
        // Entry line reveal helpers
        // ====================================================================

        private void ShowCardNumber()
        {
            if (CardNumberBox == null || CardNumberPlainTextBox == null || BtnViewCardNumber == null)
                return;

            _isCardNumberRevealed = true;

            string trimmed = TrimToMaxChars(CardNumberBox.Password);
            if (!string.Equals(trimmed, CardNumberBox.Password, StringComparison.Ordinal))
                CardNumberBox.Password = trimmed;

            CardNumberPlainTextBox.Text = trimmed;
            CardNumberPlainTextBox.Visibility = Visibility.Visible;

            CardNumberBox.Visibility = Visibility.Collapsed;

            BtnViewCardNumber.ToolTip = "Hide card number";

            TouchRevealTimerIfNeeded();
        }

        private void HideCardNumber(bool copyBack, bool clearOverlay)
        {
            if (CardNumberBox == null || CardNumberPlainTextBox == null || BtnViewCardNumber == null)
                return;

            if (!_isCardNumberRevealed && CardNumberBox.Visibility == Visibility.Visible)
                return;

            _isCardNumberRevealed = false;

            if (copyBack)
            {
                string s = TrimToMaxChars(CardNumberPlainTextBox.Text);
                if (!string.Equals(s, CardNumberBox.Password, StringComparison.Ordinal))
                    CardNumberBox.Password = s;
            }

            CardNumberBox.Visibility = Visibility.Visible;

            if (clearOverlay)
                ClearRevealOverlayTextOnly(CardNumberPlainTextBox);

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

        private void HideCvv(bool copyBack, bool clearOverlay)
        {
            if (CvvBox == null || CvvPlainTextBox == null || BtnToggleCvvReveal == null)
                return;

            if (!_isCvvRevealed && CvvBox.Visibility == Visibility.Visible)
                return;

            _isCvvRevealed = false;

            if (copyBack)
                CvvBox.Password = CvvPlainTextBox.Text ?? string.Empty;

            CvvBox.Visibility = Visibility.Visible;

            if (clearOverlay)
                ClearRevealOverlayTextOnly(CvvPlainTextBox);

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

        private void HidePin(bool copyBack, bool clearOverlay)
        {
            if (PinBox == null || PinPlainTextBox == null || BtnTogglePinReveal == null)
                return;

            if (!_isPinRevealed && PinBox.Visibility == Visibility.Visible)
                return;

            _isPinRevealed = false;

            if (copyBack)
                PinBox.Password = PinPlainTextBox.Text ?? string.Empty;

            PinBox.Visibility = Visibility.Visible;

            if (clearOverlay)
                ClearRevealOverlayTextOnly(PinPlainTextBox);

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

            string trimmed = TrimToMaxChars(CardNumberBox.Password);
            if (!string.Equals(trimmed, CardNumberBox.Password, StringComparison.Ordinal))
                CardNumberBox.Password = trimmed;

            if (_isCardNumberRevealed && CardNumberPlainTextBox != null)
            {
                _suppressPlainTextNormalize = true;
                try { CardNumberPlainTextBox.Text = trimmed; }
                finally { _suppressPlainTextNormalize = false; }
            }

            MarkDirty();
            TouchRevealTimerIfNeeded();
        }

        private void CardNumberPlainTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isCardNumberRevealed || CardNumberPlainTextBox == null)
                return;

            if (_suppressPlainTextNormalize)
                return;

            string trimmed = TrimToMaxChars(CardNumberPlainTextBox.Text);
            if (!string.Equals(trimmed, CardNumberPlainTextBox.Text, StringComparison.Ordinal))
            {
                _suppressPlainTextNormalize = true;
                try
                {
                    CardNumberPlainTextBox.Text = trimmed;
                    CardNumberPlainTextBox.SelectionStart = trimmed.Length;
                }
                finally
                {
                    _suppressPlainTextNormalize = false;
                }
            }

            MarkDirty();
            TouchRevealTimerIfNeeded();
        }

        private void CvvBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_isCvvRevealed && CvvPlainTextBox != null)
            {
                _suppressPlainTextNormalize = true;
                try { CvvPlainTextBox.Text = CvvBox?.Password ?? string.Empty; }
                finally { _suppressPlainTextNormalize = false; }
            }

            MarkDirty();
            TouchRevealTimerIfNeeded();
        }

        private void CvvPlainTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isCvvRevealed || _suppressPlainTextNormalize)
                return;

            MarkDirty();
            TouchRevealTimerIfNeeded();
        }

        private void PinBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_isPinRevealed && PinPlainTextBox != null)
            {
                _suppressPlainTextNormalize = true;
                try { PinPlainTextBox.Text = PinBox?.Password ?? string.Empty; }
                finally { _suppressPlainTextNormalize = false; }
            }

            MarkDirty();
            TouchRevealTimerIfNeeded();
        }

        private void PinPlainTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isPinRevealed || _suppressPlainTextNormalize)
                return;

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

            if (BankCardGrid != null && BankCardGrid.SelectedItem is BankCardRow row)
            {
                _suppressDirty = true;
                try
                {
                    SetEditingMode(null);

                    var cardType = _cardTypeItems.FirstOrDefault(ct => ct.ComboDetailId == row.CardTypeId);
                    if (CardTypeCombo != null)
                    {
                        if (cardType != null) CardTypeCombo.SelectedItem = cardType;
                        else CardTypeCombo.SelectedIndex = _cardTypeItems.Count > 0 ? 0 : -1;
                    }

                    if (CardNumberBox != null)
                        CardNumberBox.Password = TrimToMaxChars(row.CardNumberRaw ?? string.Empty);

                    if (ExpirationTextBox != null)
                        ExpirationTextBox.Text = row.Expiration ?? string.Empty;

                    if (CvvBox != null) UICleaner.Clear(CvvBox);
                    HideCvv(copyBack: false, clearOverlay: true);

                    if (PinBox != null) UICleaner.Clear(PinBox);
                    HidePin(copyBack: false, clearOverlay: true);

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

            if (_isCardNumberRevealed)
            {
                HideCardNumber(copyBack: true, clearOverlay: true);
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
            if (_isCvvRevealed) HideCvv(copyBack: true, clearOverlay: true);
            else ShowCvv();
        }

        private void BtnTogglePinReveal_Click(object sender, RoutedEventArgs e)
        {
            if (_isPinRevealed) HidePin(copyBack: true, clearOverlay: true);
            else ShowPin();
        }

        // ====================================================================
        // Row-level button handlers
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

            string cvv = GetCurrentCvv();
            string pin = GetCurrentPin();

            bool isActive = ChkCardActive?.IsChecked == true;

            if (!TryValidateExpiration(expirationInput, out string expNormalized, out _))
            {
                ShowBankCardError("Expiration date is invalid.", ExpirationTextBox);
                SetErrors(true);
                UpdateTabButtons();
                return;
            }

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

            ClearEntryFields();
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
                SetEditingMode(row);

                var cardType = _cardTypeItems.FirstOrDefault(ct => ct.ComboDetailId == row.CardTypeId);
                if (CardTypeCombo != null && cardType != null)
                    CardTypeCombo.SelectedItem = cardType;

                if (CardNumberBox != null)
                    CardNumberBox.Password = TrimToMaxChars(row.CardNumberRaw);
                HideCardNumber(copyBack: false, clearOverlay: true);

                if (ExpirationTextBox != null)
                    ExpirationTextBox.Text = row.Expiration ?? string.Empty;

                if (CvvBox != null)
                    CvvBox.Password = row.CvvRaw ?? string.Empty;
                HideCvv(copyBack: false, clearOverlay: true);

                if (PinBox != null)
                    PinBox.Password = row.PinRaw ?? string.Empty;
                HidePin(copyBack: false, clearOverlay: true);

                if (ChkCardActive != null)
                    ChkCardActive.IsChecked = row.IsActive;

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

            if (row.Id != 0)
            {
                ShowBankCardError("Existing cards can't be deleted here. Edit the card or mark it inactive instead.");
                SetErrors(true);
                UpdateTabButtons();
                return;
            }

            if (_editingRow == row)
                ClearEntryFields();

            // Wipe the row before removing it.
            row.Wipe();

#if DEBUG
            DebugVerifyRowWiped(row, context: "DELETE-ROW");
#endif

            _bankCardRows.Remove(row);

            SetErrors(false);
            MarkDirty();
            UpdateTabButtons();
        }

        // ====================================================================
        // TAB-LEVEL Save/Cancel handlers
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

            RaiseSaveAndExitRequestOnly();
        }

        private void OnTabCancelClick(object sender, RoutedEventArgs e)
        {
            ClearBankCardError();

            HideRevealsAndStopTimer(copyBack: false, clearRevealOverlays: true);
            WipeSensitiveEntryFields();
            ResetBankCardFieldBackgrounds();

            CancelAndExitRequested?.Invoke(this, EventArgs.Empty);
        }

        private void RaiseSaveAndExitRequestOnly()
        {
            var payload = _bankCardRows.Select(CloneRow).ToList();
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

            string cvv = GetCurrentCvv();
            string pin = GetCurrentPin();

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
            HideRevealsAndStopTimer(copyBack: false, clearRevealOverlays: true);

            if (CardNumberBox != null) UICleaner.Clear(CardNumberBox);
            if (CvvBox != null) UICleaner.Clear(CvvBox);
            if (PinBox != null) UICleaner.Clear(PinBox);

            ClearRevealOverlayTextOnly(CardNumberPlainTextBox);
            ClearRevealOverlayTextOnly(CvvPlainTextBox);
            ClearRevealOverlayTextOnly(PinPlainTextBox);

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
                HideCardNumber(copyBack: false, clearOverlay: true);

                if (ExpirationTextBox != null) UICleaner.Clear(ExpirationTextBox);

                if (CvvBox != null) UICleaner.Clear(CvvBox);
                HideCvv(copyBack: false, clearOverlay: true);

                if (PinBox != null) UICleaner.Clear(PinBox);
                HidePin(copyBack: false, clearOverlay: true);

                if (ChkCardActive != null)
                    ChkCardActive.IsChecked = true;

                SetEditingMode(null);

                ClearBankCardError();
                ResetBankCardFieldBackgrounds();
            }
            finally
            {
                _suppressDirty = false;
            }
        }

        /// <summary>
        /// Strong wipe for the grid: wipe each row then clear the collection.
        /// Uses the common wiping interface so other tabs can reuse the same pattern.
        /// </summary>
        private void WipeAndClearBankCardRows()
        {
#if DEBUG
            int before = _bankCardRows.Count;
#endif
            // Use the security DLL common wiper if you added it; otherwise, this still works
            // because rows implement ISensitiveWipe.
            foreach (var row in _bankCardRows.ToList())
                row.Wipe();

            _bankCardRows.Clear();

#if DEBUG
            Debug.WriteLine($"[BANK-CARDS-PANEL][WIPE] Grid wiped+cleared. Rows before={before}, after={_bankCardRows.Count}");
#endif
        }

        // ====================================================================
        // Helpers
        // ====================================================================

        private void SetEditingMode(BankCardRow? row)
        {
            _editingRow = row;

            if (BtnBankCardAddOrUpdate != null)
                BtnBankCardAddOrUpdate.Content = (_editingRow == null) ? "Add" : "Update";
        }

        private bool EntryLineHasAnyInput()
        {
            if (_isCardNumberRevealed)
            {
                if (CardNumberPlainTextBox != null && !string.IsNullOrWhiteSpace(CardNumberPlainTextBox.Text))
                    return true;
            }
            else
            {
                if (CardNumberBox != null && !string.IsNullOrWhiteSpace(CardNumberBox.Password))
                    return true;
            }

            if (ExpirationTextBox != null && !string.IsNullOrWhiteSpace(ExpirationTextBox.Text))
                return true;

            if (_isCvvRevealed)
            {
                if (CvvPlainTextBox != null && !string.IsNullOrWhiteSpace(CvvPlainTextBox.Text))
                    return true;
            }
            else
            {
                if (CvvBox != null && !string.IsNullOrWhiteSpace(CvvBox.Password))
                    return true;
            }

            if (_isPinRevealed)
            {
                if (PinPlainTextBox != null && !string.IsNullOrWhiteSpace(PinPlainTextBox.Text))
                    return true;
            }
            else
            {
                if (PinBox != null && !string.IsNullOrWhiteSpace(PinBox.Password))
                    return true;
            }

            return false;
        }

        private string GetCurrentCardNumber()
        {
            if (_isCardNumberRevealed && CardNumberPlainTextBox != null)
                return TrimToMaxChars(CardNumberPlainTextBox.Text).Trim();

            if (CardNumberBox == null)
                return string.Empty;

            return TrimToMaxChars(CardNumberBox.Password).Trim();
        }

        private string GetCurrentCvv()
        {
            return _isCvvRevealed
                ? (CvvPlainTextBox?.Text ?? string.Empty)
                : (CvvBox?.Password ?? string.Empty);
        }

        private string GetCurrentPin()
        {
            return _isPinRevealed
                ? (PinPlainTextBox?.Text ?? string.Empty)
                : (PinBox?.Password ?? string.Empty);
        }

        private static string TrimToMaxChars(string? value)
        {
            var s = value ?? string.Empty;
            return s.Length <= MaxCardNumberChars ? s : s.Substring(0, MaxCardNumberChars);
        }

        private void HookUiEventsOnce()
        {
            if (_uiEventsHooked)
                return;

            if (CardNumberBox != null)
            {
                CardNumberBox.PasswordChanged -= CardNumberBox_PasswordChanged;
                CardNumberBox.PasswordChanged += CardNumberBox_PasswordChanged;
            }

            if (CardNumberPlainTextBox != null)
            {
                CardNumberPlainTextBox.TextChanged -= CardNumberPlainTextBox_TextChanged;
                CardNumberPlainTextBox.TextChanged += CardNumberPlainTextBox_TextChanged;
            }

            if (CvvBox != null)
            {
                CvvBox.PasswordChanged -= CvvBox_PasswordChanged;
                CvvBox.PasswordChanged += CvvBox_PasswordChanged;
            }

            if (CvvPlainTextBox != null)
            {
                CvvPlainTextBox.TextChanged -= CvvPlainTextBox_TextChanged;
                CvvPlainTextBox.TextChanged += CvvPlainTextBox_TextChanged;
            }

            if (PinBox != null)
            {
                PinBox.PasswordChanged -= PinBox_PasswordChanged;
                PinBox.PasswordChanged += PinBox_PasswordChanged;
            }

            if (PinPlainTextBox != null)
            {
                PinPlainTextBox.TextChanged -= PinPlainTextBox_TextChanged;
                PinPlainTextBox.TextChanged += PinPlainTextBox_TextChanged;
            }

            _uiEventsHooked = true;
        }

        private void UnhookUiEvents()
        {
            if (!_uiEventsHooked)
                return;

            if (CardNumberBox != null)
                CardNumberBox.PasswordChanged -= CardNumberBox_PasswordChanged;

            if (CardNumberPlainTextBox != null)
                CardNumberPlainTextBox.TextChanged -= CardNumberPlainTextBox_TextChanged;

            if (CvvBox != null)
                CvvBox.PasswordChanged -= CvvBox_PasswordChanged;

            if (CvvPlainTextBox != null)
                CvvPlainTextBox.TextChanged -= CvvPlainTextBox_TextChanged;

            if (PinBox != null)
                PinBox.PasswordChanged -= PinBox_PasswordChanged;

            if (PinPlainTextBox != null)
                PinPlainTextBox.TextChanged -= PinPlainTextBox_TextChanged;

            _uiEventsHooked = false;
        }

        private static void ForceGcBestEffort()
        {
            try
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            }
            catch
            {
                // Never crash due to "best effort" GC.
            }
        }

#if DEBUG
        private static void DebugVerifyRowWiped(BankCardRow row, string context)
        {
            bool ok =
                (row.CardTypeId == 0) &&
                string.IsNullOrEmpty(row.CardTypeDisplay) &&
                string.IsNullOrEmpty(row.CardNumberRaw) &&
                string.IsNullOrEmpty(row.Expiration) &&
                string.IsNullOrEmpty(row.CvvRaw) &&
                string.IsNullOrEmpty(row.PinRaw) &&
                (row.IsActive == false);

            Debug.WriteLine($"[BANK-CARDS-PANEL][VERIFY][{context}] Row wiped = {ok}");
        }
#endif

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

        public sealed class BankCardRow : INotifyPropertyChanged, ISensitiveWipe
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

            public void Wipe()
            {
                // Best-effort managed wipe: drop references immediately.
                CardTypeDisplay = string.Empty;
                CardTypeId = 0;
                CardNumberRaw = string.Empty;
                Expiration = string.Empty;
                CvvRaw = string.Empty;
                PinRaw = string.Empty;
                IsActive = false;
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged(string propertyName) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
