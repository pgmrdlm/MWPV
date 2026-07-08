// File: View/UserControls/CategoryItems/CategoryItemBankCardsPanel.xaml.cs
//
// FULL REWRITE (match current XAML exactly)
//
// Notes:
// - XAML has NO Primary/BillingZip controls.
// - XAML includes a FREEFORM-only custom card-name textbox, but panel persistence wiring remains separate.
// - Grid edit/delete are "selected row" buttons (no per-row Tag buttons).
// - Grid bindings expect: CardTypeDisplay, CardNumberMasked, Expiration, CvvMasked, PinMasked, IsActive.
// - This panel maintains its own UI row DTO (BankCardRow) with Raw + Masked fields.
// - Host can load rows (from service select) via LoadFromHostRows(...).
// - Save raises SaveAndExitRequested with payload rows (raw values included).
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
    public partial class CategoryItemBankCardsPanel : UserControl
    {
        // ============================================================
        // Events for host
        // ============================================================

        public event EventHandler<BankCardsCommitEventArgs>? SaveAndExitRequested;
        public event EventHandler? CancelAndExitRequested;

        // ============================================================
        // Collections (binding)
        // ============================================================

        private readonly ObservableCollection<BankCardRow> _bankCardRows = new();
        private readonly ObservableCollection<CardTypeItem> _cardTypeItems = new();

        public ObservableCollection<BankCardRow> BankCardRows => _bankCardRows;
        public ObservableCollection<CardTypeItem> CardTypeItems => _cardTypeItems;

        private BankCardRow? _editingRow;

        // ============================================================
        // Tab state
        // ============================================================

        private readonly List<BankCardRow> _baselineRows = new();

        private bool _hasChanges;
        private bool _hasErrors;
        private bool _newCardSessionStarted;

        public bool HasChanges => _hasChanges;
        public bool HasErrors => _hasErrors;

        private bool _suppressDirty;
        private bool _entryDisabled;

        // ============================================================
        // Reveal state + timer (read-only overlays)
        // ============================================================

        private bool _isCardNumberRevealed;
        private bool _isCvvRevealed;
        private bool _isPinRevealed;

        private readonly AutoHideTimer _revealAutoHide;
        private bool _uiEventsHooked;

        // Host-close guard
        private bool _hostRequestedCloseWipe;

        private const int MaxCardNumberChars = 19; // digits + spaces
        private const string FreeformCardTypeCode = "FREEFORM";

        private const string SedsKey_BankCardSelectedCardId = "BC.Selected.CardId";
        private const string SedsKey_BankCardSelectedNumber = "BC.Selected.Number";
        private const string SedsKey_BankCardSelectedCvv = "BC.Selected.CVV";
        private const string SedsKey_BankCardSelectedPin = "BC.Selected.Pin";
        private const string SedsKey_BankCardSelectedBillingZip = "BC.Selected.BillingZip";

        private bool _isSelectedProtectedViewActive;
        // ============================================================
        // Ctor
        // ============================================================

        public CategoryItemBankCardsPanel()
        {
            InitializeComponent();

            DataContext = this;

            Loaded += CategoryItemBankCardsPanel_Loaded;
            Unloaded += CategoryItemBankCardsPanel_Unloaded;

            _revealAutoHide = new AutoHideTimer(
                interval: TimeSpan.FromSeconds(20),
                onTimeout: OnRevealTimeout);

            _bankCardRows.CollectionChanged += BankCardRows_CollectionChanged;
        }

        // ============================================================
        // HOST API
        // ============================================================

        /// <summary>
        /// Host loads rows into this panel (typically from CategoryItemService.LoadBankCardsByItemId).
        /// </summary>
        public void LoadFromHostRows(IEnumerable<CategoryItemService.BankCardRow>? rows)
        {
            _suppressDirty = true;
            try
            {
                ClearNewCardSessionTracking("LoadFromHostRows");
                HideRevealsAndStopTimer(clearRevealOverlays: true);
                WipeSensitiveEntryFields();
                ClearBankCardError();
                ResetBankCardFieldBackgrounds();
                ClearSelectedBankCardDetailSedsBestEffort();
                _isSelectedProtectedViewActive = false;

                if (BankCardGrid != null)
                    BankCardGrid.SelectedItem = null;

                DetachRowHandlers(_bankCardRows);
                WipeAndClearBankCardRows();

                if (rows != null)
                {
                    foreach (var r in rows)
                    {
                        var ui = new BankCardRow
                        {
                            Id = r.Id,
                            CardTypeId = r.CardTypeId,
                            CardTypeDisplay = r.CardTypeDisplay ?? string.Empty,
                            Cardholder = r.Cardholder ?? string.Empty,
                            IsFreeform = IsFreeformCardTypeId(r.CardTypeId),

                            // Service never returns plaintext. Keep raw empty.
                            CardNumberRaw = string.Empty,
                            CvvRaw = string.Empty,
                            PinRaw = string.Empty,

                            // Service provides display
                            Expiration = r.ExpirationDisplay ?? string.Empty,

                            IsActive = r.IsActive,

                            // For display
                            CardNumberMasked = r.CardNumberMasked ?? string.Empty,
                            CvvMasked = r.CvvMasked ?? string.Empty,
                            PinMasked = r.PinMasked ?? string.Empty
                        };

                        AttachRowHandlers(ui);
                        _bankCardRows.Add(ui);
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
            ClearNewCardSessionTracking("WipeAllForHostClose");

            HideRevealsAndStopTimer(clearRevealOverlays: true);
            WipeSensitiveEntryFields();
            WipeAndClearBankCardRows();
            ClearSelectedBankCardDetailSedsBestEffort();
            _isSelectedProtectedViewActive = false;

            ClearBankCardError();
            ResetBankCardFieldBackgrounds();

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
            ClearNewCardSessionTracking("TryAutoCommitAndWipe");
            ClearEntryFields();
            ClearSelectedBankCardDetailSedsBestEffort();
            _isSelectedProtectedViewActive = false;
            SetErrors(false);
            UpdateTabButtons();
            return true;
        }

        /// <summary>
        /// HOST-CLOSE behavior ONLY:
        /// Returns true when BankCards currently has meaningful session work that should
        /// drive an explicit host-close decision before the window is allowed to close.
        /// </summary>
        public bool HasHostCloseSessionWork()
        {
            bool hasWork =
                _newCardSessionStarted ||
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
        public bool TryBuildHostCloseSavePayload(out IReadOnlyList<BankCardRow> rows)
        {
            rows = Array.Empty<BankCardRow>();

            ClearBankCardError();

            if (_entryDisabled)
            {
                ShowBankCardError("Bank card entry is disabled for this session.");
                SetErrors(true);
                UpdateTabButtons();
                return false;
            }

            if (EntryLineHasAnyInput())
            {
                ShowBankCardError("Finish Add/Update or Clear Row before saving.");
                SetErrors(true);
                UpdateTabButtons();
                return false;
            }

            if (HasPendingBlankAddAttempt())
            {
                ShowBankCardError("Finish Add/Update or Clear Row before saving.");
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
            SetErrors(false);
            UpdateTabButtons();

            rows = _bankCardRows.Select(CloneRow).ToList();

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
                ClearNewCardSessionTracking("TryPrepareHostCloseDiscard");
                ClearBankCardError();
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

        private void CategoryItemBankCardsPanel_Loaded(object? sender, RoutedEventArgs e)
        {
            HookUiEventsOnce();

            if (CardNumberBox != null)
                CardNumberBox.MaxLength = MaxCardNumberChars;

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

            HideRevealsAndStopTimer(clearRevealOverlays: true);
            WipeSensitiveEntryFields();
            ClearSelectedBankCardDetailSedsBestEffort();
            _isSelectedProtectedViewActive = false;

            if (_hostRequestedCloseWipe)
                WipeAndClearBankCardRows();
        }

        // ============================================================
        // Dirty tracking
        // ============================================================

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
            foreach (var r in _bankCardRows)
                _baselineRows.Add(CloneRow(r));
        }

        private void RestoreBaseline()
        {
            _suppressDirty = true;
            try
            {
                ClearNewCardSessionTracking("RestoreBaseline");
                HideRevealsAndStopTimer(clearRevealOverlays: true);
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
                Cardholder = r.Cardholder ?? string.Empty,
                IsFreeform = r.IsFreeform,

                CardNumberRaw = r.CardNumberRaw ?? string.Empty,
                Expiration = r.Expiration ?? string.Empty,
                CvvRaw = r.CvvRaw ?? string.Empty,
                PinRaw = r.PinRaw ?? string.Empty,

                IsActive = r.IsActive,

                CardNumberMasked = r.CardNumberMasked ?? string.Empty,
                CvvMasked = r.CvvMasked ?? string.Empty,
                PinMasked = r.PinMasked ?? string.Empty
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
            bool anyRevealed = _isCardNumberRevealed || _isCvvRevealed || _isPinRevealed;
            _revealAutoHide.Touch(anyRevealed);
        }

        private void HideRevealsAndStopTimer(bool clearRevealOverlays)
        {
            _revealAutoHide.Stop();
            HideCardNumber(clearOverlay: clearRevealOverlays);
            HideCvv(clearOverlay: clearRevealOverlays);
            HidePin(clearOverlay: clearRevealOverlays);
        }

        private static void ClearRevealOverlayTextOnly(TextBox? tb)
        {
            if (tb == null) return;
            tb.Text = string.Empty;
        }

        // ============================================================
        // Combos
        // ============================================================

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
                UpdateCustomCardNameUi(clearWhenHidden: false);

                SetErrors(false);
                UpdateTabButtons();
            }
            catch (Exception ex)
            {
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

            UpdateCustomCardNameUi(clearWhenHidden: false);
        }

        private void ApplyProtectedViewControlState()
        {
            bool editable = !_entryDisabled && !_isSelectedProtectedViewActive;

            if (CardTypeCombo != null) CardTypeCombo.IsEnabled = editable;
            if (CardNumberBox != null) CardNumberBox.IsEnabled = editable;
            if (ExpirationTextBox != null) ExpirationTextBox.IsEnabled = editable;
            if (CvvBox != null) CvvBox.IsEnabled = editable;
            if (PinBox != null) PinBox.IsEnabled = editable;
            if (ChkCardActive != null) ChkCardActive.IsEnabled = editable;

            // Keep reveal/view available in protected view; disable only when entry is globally disabled.
            bool allowReveal = !_entryDisabled;
            if (BtnViewCardNumber != null) BtnViewCardNumber.IsEnabled = allowReveal;
            if (BtnToggleCvvReveal != null) BtnToggleCvvReveal.IsEnabled = allowReveal;
            if (BtnTogglePinReveal != null) BtnTogglePinReveal.IsEnabled = allowReveal;

            bool allowCopy = !_entryDisabled && _isSelectedProtectedViewActive;
            if (BtnCopyCardNumber != null)
            {
                BtnCopyCardNumber.Visibility = allowCopy ? Visibility.Visible : Visibility.Collapsed;
                BtnCopyCardNumber.IsEnabled = allowCopy;
            }

            if (BtnCopyCvv != null)
            {
                BtnCopyCvv.Visibility = allowCopy ? Visibility.Visible : Visibility.Collapsed;
                BtnCopyCvv.IsEnabled = allowCopy;
            }

            if (BtnCopyPin != null)
            {
                BtnCopyPin.Visibility = allowCopy ? Visibility.Visible : Visibility.Collapsed;
                BtnCopyPin.IsEnabled = allowCopy;
            }

            bool hasSelectedExistingRow = BankCardGrid?.SelectedItem is BankCardRow selected && selected.Id > 0;
            bool showEditSelected = !_entryDisabled && _editingRow == null && _isSelectedProtectedViewActive && hasSelectedExistingRow;
            if (BtnBankCardEditSelected != null)
            {
                BtnBankCardEditSelected.Visibility = showEditSelected ? Visibility.Visible : Visibility.Collapsed;
                BtnBankCardEditSelected.IsEnabled = showEditSelected;
            }

            if (BtnBankCardAddOrUpdate != null) BtnBankCardAddOrUpdate.IsEnabled = editable;
            if (BtnBankCardClearRow != null) BtnBankCardClearRow.IsEnabled = editable;
            UpdateCustomCardNameUi(clearWhenHidden: false);
        }

        private static bool IsFreeformCardTypeCode(string? code)
        {
            return string.Equals((code ?? string.Empty).Trim(), FreeformCardTypeCode, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsFreeformCardType(CardTypeItem? item)
        {
            return item != null && IsFreeformCardTypeCode(item.Code);
        }

        private bool IsFreeformCardTypeId(int cardTypeId)
        {
            return IsFreeformCardType(_cardTypeItems.FirstOrDefault(ct => ct.ComboDetailId == cardTypeId));
        }

        private string GetCurrentCustomCardName()
        {
            return (CustomCardNameTextBox?.Text ?? string.Empty).Trim();
        }

        private void UpdateCustomCardNameUi(bool clearWhenHidden)
        {
            bool isFreeformSelection = IsFreeformCardType(CardTypeCombo?.SelectedItem as CardTypeItem);
            bool showCustomCardName = isFreeformSelection && !_isSelectedProtectedViewActive;

            if (CustomCardNamePanel != null)
                CustomCardNamePanel.Visibility = showCustomCardName ? Visibility.Visible : Visibility.Collapsed;

            if (CustomCardNameTextBox != null)
            {
                CustomCardNameTextBox.IsEnabled = showCustomCardName && !_entryDisabled;

                if (!showCustomCardName && clearWhenHidden)
                    UICleaner.Clear(CustomCardNameTextBox);
            }
        }

        private void CardTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool isFreeformSelection = IsFreeformCardType(CardTypeCombo?.SelectedItem as CardTypeItem);
            UpdateCustomCardNameUi(clearWhenHidden: !isFreeformSelection);

            if (_suppressDirty)
                return;

            if (_entryDisabled)
            {
                SetErrors(true);
                UpdateTabButtons();
                return;
            }

            if (!EntryLineHasAnyInput())
            {
                ClearBankCardError();
                SetErrors(false);
                UpdateTabButtons();
                return;
            }

            bool ok = ValidateBankCardFields(showErrors: true, out _, out _, out _);
            SetErrors(!ok);
            UpdateTabButtons();
        }

        // ============================================================
        // Reveal helpers (read-only overlays; no editing in overlay)
        // ============================================================

        private void ShowCardNumber()
        {
            if (CardNumberBox == null || CardNumberPlainTextBox == null || BtnViewCardNumber == null)
                return;

            _isCardNumberRevealed = true;

            string trimmed = TrimToMaxChars(CardNumberBox.Password);
            if (!string.Equals(trimmed, CardNumberBox.Password, StringComparison.Ordinal))
                CardNumberBox.Password = trimmed;

            MaskedRevealOverlayHelper.ShowPlainOverlay(CardNumberBox, CardNumberPlainTextBox, trimmed);

            BtnViewCardNumber.ToolTip = "Hide card number";

            TouchRevealTimerIfNeeded();
        }

        private void HideCardNumber(bool clearOverlay)
        {
            if (CardNumberBox == null || CardNumberPlainTextBox == null || BtnViewCardNumber == null)
                return;

            if (!_isCardNumberRevealed && CardNumberBox.Visibility == Visibility.Visible)
                return;

            _isCardNumberRevealed = false;

            CardNumberBox.Visibility = Visibility.Visible;

            if (clearOverlay)
                ClearRevealOverlayTextOnly(CardNumberPlainTextBox);

            MaskedRevealOverlayHelper.RestoreMaskedOverlay(CardNumberBox, CardNumberPlainTextBox);

            BtnViewCardNumber.ToolTip = "Show card number";
            TouchRevealTimerIfNeeded();
        }

        private void ShowCvv()
        {
            if (CvvBox == null || CvvPlainTextBox == null || BtnToggleCvvReveal == null)
                return;

            _isCvvRevealed = true;

            MaskedRevealOverlayHelper.ShowPlainOverlay(CvvBox, CvvPlainTextBox, CvvBox.Password);

            BtnToggleCvvReveal.ToolTip = "Hide CVV";
            TouchRevealTimerIfNeeded();
        }

        private void HideCvv(bool clearOverlay)
        {
            if (CvvBox == null || CvvPlainTextBox == null || BtnToggleCvvReveal == null)
                return;

            if (!_isCvvRevealed && CvvBox.Visibility == Visibility.Visible)
                return;

            _isCvvRevealed = false;

            CvvBox.Visibility = Visibility.Visible;

            if (clearOverlay)
                ClearRevealOverlayTextOnly(CvvPlainTextBox);

            MaskedRevealOverlayHelper.RestoreMaskedOverlay(CvvBox, CvvPlainTextBox);

            BtnToggleCvvReveal.ToolTip = "Show CVV";
            TouchRevealTimerIfNeeded();
        }

        private void ShowPin()
        {
            if (PinBox == null || PinPlainTextBox == null || BtnTogglePinReveal == null)
                return;

            _isPinRevealed = true;

            MaskedRevealOverlayHelper.ShowPlainOverlay(PinBox, PinPlainTextBox, PinBox.Password);

            BtnTogglePinReveal.ToolTip = "Hide card PIN";
            TouchRevealTimerIfNeeded();
        }

        private void HidePin(bool clearOverlay)
        {
            if (PinBox == null || PinPlainTextBox == null || BtnTogglePinReveal == null)
                return;

            if (!_isPinRevealed && PinBox.Visibility == Visibility.Visible)
                return;

            _isPinRevealed = false;

            PinBox.Visibility = Visibility.Visible;

            if (clearOverlay)
                ClearRevealOverlayTextOnly(PinPlainTextBox);

            MaskedRevealOverlayHelper.RestoreMaskedOverlay(PinBox, PinPlainTextBox);

            BtnTogglePinReveal.ToolTip = "Show card PIN";
            TouchRevealTimerIfNeeded();
        }

        // ============================================================
        // Entry change handlers (dirty + timer)
        // ============================================================

        private void CardNumberBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (CardNumberBox == null)
                return;

            string trimmed = TrimToMaxChars(CardNumberBox.Password);
            if (!string.Equals(trimmed, CardNumberBox.Password, StringComparison.Ordinal))
                CardNumberBox.Password = trimmed;

            if (_isCardNumberRevealed && CardNumberPlainTextBox != null)
                CardNumberPlainTextBox.Text = trimmed;

            MarkDirty();
            TouchRevealTimerIfNeeded();
        }

        private void CvvBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_isCvvRevealed && CvvPlainTextBox != null)
                CvvPlainTextBox.Text = CvvBox?.Password ?? string.Empty;

            MarkDirty();
            TouchRevealTimerIfNeeded();
        }

        private void PinBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_isPinRevealed && PinPlainTextBox != null)
                PinPlainTextBox.Text = PinBox?.Password ?? string.Empty;

            MarkDirty();
            TouchRevealTimerIfNeeded();
        }

        // ============================================================
        // Reveal button handlers
        // ============================================================

        private void BtnViewCardNumber_Click(object sender, RoutedEventArgs e)
        {
            ClearBankCardError();
            ResetBankCardFieldBackgrounds();

            if (_isCardNumberRevealed) HideCardNumber(clearOverlay: true);
            else ShowCardNumber();
        }

        private void BtnToggleCvvReveal_Click(object sender, RoutedEventArgs e)
        {
            if (_isCvvRevealed) HideCvv(clearOverlay: true);
            else ShowCvv();
        }

        private void BtnTogglePinReveal_Click(object sender, RoutedEventArgs e)
        {
            if (_isPinRevealed) HidePin(clearOverlay: true);
            else ShowPin();
        }

        // ============================================================
        // Protected-view copy buttons
        // ============================================================
        private void BtnCopyCardNumber_Click(object sender, RoutedEventArgs e)
        {
            CopySelectedProtectedFieldToClipboard(SedsKey_BankCardSelectedNumber, "No card number is available to copy.");
        }

        private void BtnCopyCvv_Click(object sender, RoutedEventArgs e)
        {
            CopySelectedProtectedFieldToClipboard(SedsKey_BankCardSelectedCvv, "No CVV is available to copy.");
        }

        private void BtnCopyPin_Click(object sender, RoutedEventArgs e)
        {
            CopySelectedProtectedFieldToClipboard(SedsKey_BankCardSelectedPin, "No card PIN is available to copy.");
        }

        private void CopySelectedProtectedFieldToClipboard(string sedsKey, string emptyMessage)
        {
            if (!_isSelectedProtectedViewActive || _entryDisabled)
                return;

            string value = ReadSelectedProtectedFieldFromSeds(sedsKey);
            if (string.IsNullOrWhiteSpace(value))
            {
                ShowBankCardError(emptyMessage);
                return;
            }

            string reasonCode = sedsKey switch
            {
                SedsKey_BankCardSelectedNumber => "BANKCARD.NUMBER",
                SedsKey_BankCardSelectedCvv => "BANKCARD.CVV",
                SedsKey_BankCardSelectedPin => "BANKCARD.PIN",
                _ => "BANKCARD.SENSITIVE"
            };

            if (ClipboardHelper.TryCopySensitiveText(value, out _, tag: reasonCode))
                ClearBankCardError();
            else
                ShowBankCardError("Unable to copy value to clipboard.");
        }
        // ============================================================
        // Row-level: Add/Update + Clear
        // ============================================================

        private void OnBankCardAddOrUpdateClick(object sender, RoutedEventArgs e)
        {
            _isSelectedProtectedViewActive = false;
            ClearBankCardError();

            bool isTrueAddMode = _editingRow == null;
            bool isUpdate = _editingRow != null;
            bool isExistingPersistedUpdate = _editingRow != null && _editingRow.Id > 0;

            if (isTrueAddMode)
            {
                BeginAddNewCardSession();
            }

            if (_entryDisabled)
            {
                ShowBankCardError("Bank card entry is disabled for this session.");
                SetErrors(true);
                UpdateTabButtons();
                return;
            }

            if (!ValidateBankCardFields(showErrors: true, out var expMonth, out var expYear, out var expNormalized))
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
            string customCardName = IsFreeformCardType(selection) ? GetCurrentCustomCardName() : string.Empty;
            string cvv = GetCurrentCvv();
            string pin = GetCurrentPin();
            bool isActive = ChkCardActive?.IsChecked == true;

            // one card per type (except when updating the same row)
            bool duplicateType = _bankCardRows.Any(r =>
                r.CardTypeId == selection.ComboDetailId &&
                !ReferenceEquals(r, _editingRow));

            if (duplicateType)
            {
                ShowBankCardError("Only one card of each type is allowed.", CardTypeCombo);
                SetErrors(true);
                UpdateTabButtons();
                return;
            }

            string masked = MaskPanLast4(new string(cardNumber.Where(char.IsDigit).ToArray()));

            if (_editingRow == null)
            {
                var row = new BankCardRow
                {
                    Id = 0,
                    CardTypeId = selection.ComboDetailId,
                    CardTypeDisplay = selection.DisplayText,
                    Cardholder = customCardName,
                    IsFreeform = IsFreeformCardType(selection),

                    CardNumberRaw = cardNumber,
                    Expiration = expNormalized,
                    CvvRaw = cvv,
                    PinRaw = pin,

                    CardNumberMasked = masked,
                    CvvMasked = string.IsNullOrWhiteSpace(cvv) ? string.Empty : "•••",
                    PinMasked = string.IsNullOrWhiteSpace(pin) ? string.Empty : "•••",

                    IsActive = isActive
                };

                _bankCardRows.Add(row);
            }
            else
            {
                _editingRow.CardTypeId = selection.ComboDetailId;
                _editingRow.CardTypeDisplay = selection.DisplayText;
                _editingRow.Cardholder = customCardName;
                _editingRow.IsFreeform = IsFreeformCardType(selection);

                _editingRow.CardNumberRaw = cardNumber;
                _editingRow.Expiration = expNormalized;
                _editingRow.CvvRaw = cvv;
                _editingRow.PinRaw = pin;

                _editingRow.CardNumberMasked = masked;
                _editingRow.CvvMasked = string.IsNullOrWhiteSpace(cvv) ? string.Empty : "•••";
                _editingRow.PinMasked = string.IsNullOrWhiteSpace(pin) ? string.Empty : "•••";

                _editingRow.IsActive = isActive;
            }

            if (isUpdate)
                TeardownAfterSuccessfulUpdate();
            else
                ClearEntryFields();

            SetErrors(false);
            MarkDirty();
            UpdateTabButtons();

            if (isExistingPersistedUpdate || isTrueAddMode)
            {
                // Reuse the same host persistence/reload pipeline used by tab Save.
                RaiseSaveAndExitRequestOnly();
            }
        }

        private void OnBankCardClearRowClick(object sender, RoutedEventArgs e)
        {
            ClearNewCardSessionTracking("OnBankCardClearRowClick");
            _isSelectedProtectedViewActive = false;
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

            if (!EntryLineHasAnyInput())
            {
                ClearBankCardError();
                SetErrors(false);
                UpdateTabButtons();
                return;
            }

            bool ok = ValidateBankCardFields(showErrors: true, out _, out _, out _);
            SetErrors(!ok);
            UpdateTabButtons();
        }

        private void BankCardGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressDirty)
                return;

            if (BankCardGrid?.SelectedItem is not BankCardRow selected || selected.Id <= 0)
            {
                ClearSelectedBankCardDetailSedsBestEffort();
                _isSelectedProtectedViewActive = false;
                UpdateTabButtons();
                return;
            }

            int? activeItemId = TryGetActiveCategoryItemIdFromSeds();
            if (!activeItemId.HasValue || activeItemId.Value <= 0)
            {
                ClearSelectedBankCardDetailSedsBestEffort();
                _isSelectedProtectedViewActive = false;
                UpdateTabButtons();
                return;
            }

            try
            {
                var detail = CategoryItemService.LoadBankCardDetailByItemIdAndCardId(activeItemId.Value, selected.Id);
                if (detail == null)
                {
                    ClearSelectedBankCardDetailSedsBestEffort();
                    _isSelectedProtectedViewActive = false;
                    UpdateTabButtons();
                    return;
                }

                StoreSelectedBankCardDetailSedsBestEffort(detail);
                PopulateProtectedViewFromSelectedDetail(detail);

            }
            catch (Exception ex)
            {
                ClearSelectedBankCardDetailSedsBestEffort();
                _isSelectedProtectedViewActive = false;
                UpdateTabButtons();
            }
        }

        private void PopulateProtectedViewFromSelectedDetail(CategoryItemService.BankCardRow detail)
        {
            _suppressDirty = true;
            try
            {
                _isSelectedProtectedViewActive = true;
                SetEditingMode(null);

                var cardType = _cardTypeItems.FirstOrDefault(ct => ct.ComboDetailId == detail.CardTypeId);
                if (CardTypeCombo != null)
                {
                    if (cardType != null) CardTypeCombo.SelectedItem = cardType;
                    else CardTypeCombo.SelectedIndex = _cardTypeItems.Count > 0 ? 0 : -1;
                }

                if (CardNumberBox != null)
                    CardNumberBox.Password = TrimToMaxChars(detail.CardNumberRaw ?? string.Empty);
                HideCardNumber(clearOverlay: true);

                if (ExpirationTextBox != null)
                {
                    string exp = detail.ExpirationDisplay ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(exp) && detail.ExpMonth >= 1 && detail.ExpYear > 0)
                        exp = $"{detail.ExpMonth:00}/{detail.ExpYear:0000}";
                    ExpirationTextBox.Text = exp;
                }

                if (CvvBox != null)
                    CvvBox.Password = detail.CvvRaw ?? string.Empty;
                HideCvv(clearOverlay: true);

                if (PinBox != null)
                    PinBox.Password = detail.PinRaw ?? string.Empty;
                HidePin(clearOverlay: true);

                if (ChkCardActive != null)
                    ChkCardActive.IsChecked = detail.IsActive;

                if (CustomCardNameTextBox != null)
                    UICleaner.Clear(CustomCardNameTextBox);

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

        private static int? TryGetActiveCategoryItemIdFromSeds()
        {
            return CategoryItemSedsContextHelper.TryGetCurrentCategoryItemId();
        }

        private static void StoreSelectedBankCardDetailSedsBestEffort(CategoryItemService.BankCardRow detail)
        {
            ClearSelectedBankCardDetailSedsBestEffort();

            try { SecureEncryptedDataStore.SetString(SedsKey_BankCardSelectedCardId, detail.Id.ToString(CultureInfo.InvariantCulture)); } catch { }
            try { SecureEncryptedDataStore.SetString(SedsKey_BankCardSelectedNumber, detail.CardNumberRaw ?? string.Empty); } catch { }
            try { SecureEncryptedDataStore.SetString(SedsKey_BankCardSelectedCvv, detail.CvvRaw ?? string.Empty); } catch { }
            try { SecureEncryptedDataStore.SetString(SedsKey_BankCardSelectedPin, detail.PinRaw ?? string.Empty); } catch { }
            try { SecureEncryptedDataStore.SetString(SedsKey_BankCardSelectedBillingZip, detail.BillingZipRaw ?? string.Empty); } catch { }
        }

        private static void ClearSelectedBankCardDetailSedsBestEffort()
        {
            try { SecureEncryptedDataStore.Clear(SedsKey_BankCardSelectedBillingZip); } catch { }
            try { SecureEncryptedDataStore.Clear(SedsKey_BankCardSelectedPin); } catch { }
            try { SecureEncryptedDataStore.Clear(SedsKey_BankCardSelectedCvv); } catch { }
            try { SecureEncryptedDataStore.Clear(SedsKey_BankCardSelectedNumber); } catch { }
            try { SecureEncryptedDataStore.Clear(SedsKey_BankCardSelectedCardId); } catch { }
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

        private static bool TryReadSelectedProtectedDetailForEdit(long expectedCardId, out string cardNumber, out string cvv, out string pin)
        {
            cardNumber = string.Empty;
            cvv = string.Empty;
            pin = string.Empty;

            string selectedCardIdRaw = ReadSelectedProtectedFieldFromSeds(SedsKey_BankCardSelectedCardId);
            if (!long.TryParse(selectedCardIdRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long selectedCardId))
                return false;

            if (selectedCardId != expectedCardId)
                return false;

            cardNumber = ReadSelectedProtectedFieldFromSeds(SedsKey_BankCardSelectedNumber);
            if (string.IsNullOrWhiteSpace(cardNumber))
                return false;

            cvv = ReadSelectedProtectedFieldFromSeds(SedsKey_BankCardSelectedCvv);
            pin = ReadSelectedProtectedFieldFromSeds(SedsKey_BankCardSelectedPin);
            return true;
        }

        // ============================================================
        // Grid strip buttons: Edit/Delete Selected
        // ============================================================

        private void OnBankCardEditClick(object sender, RoutedEventArgs e)
        {
            if (BankCardGrid?.SelectedItem is not BankCardRow row)
                return;

            string cardNumberForEdit = row.CardNumberRaw ?? string.Empty;
            string cvvForEdit = row.CvvRaw ?? string.Empty;
            string pinForEdit = row.PinRaw ?? string.Empty;

            if (row.Id > 0)
            {
                if (!TryReadSelectedProtectedDetailForEdit(row.Id, out cardNumberForEdit, out cvvForEdit, out pinForEdit))
                {
                    ShowBankCardError("Unable to open edit mode. Reselect the row and try again.");
                    SetErrors(true);
                    UpdateTabButtons();
                    return;
                }
            }

            _isSelectedProtectedViewActive = false;

            _suppressDirty = true;
            try
            {
                SetEditingMode(row);

                var cardType = _cardTypeItems.FirstOrDefault(ct => ct.ComboDetailId == row.CardTypeId);
                if (CardTypeCombo != null)
                {
                    if (cardType != null) CardTypeCombo.SelectedItem = cardType;
                    else CardTypeCombo.SelectedIndex = _cardTypeItems.Count > 0 ? 0 : -1;
                }

                if (CardNumberBox != null)
                    CardNumberBox.Password = TrimToMaxChars(cardNumberForEdit);
                HideCardNumber(clearOverlay: true);

                if (ExpirationTextBox != null)
                    ExpirationTextBox.Text = row.Expiration ?? string.Empty;

                if (CvvBox != null)
                    CvvBox.Password = cvvForEdit;
                HideCvv(clearOverlay: true);

                if (PinBox != null)
                    PinBox.Password = pinForEdit;
                HidePin(clearOverlay: true);

                if (ChkCardActive != null)
                    ChkCardActive.IsChecked = row.IsActive;

                if (CustomCardNameTextBox != null)
                    CustomCardNameTextBox.Text = IsFreeformCardTypeId(row.CardTypeId) ? (row.Cardholder ?? string.Empty) : string.Empty;

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
            if (BankCardGrid?.SelectedItem is not BankCardRow row)
                return;

            ClearBankCardError();

            if (row.Id != 0)
            {
                ShowBankCardError("Existing cards can't be deleted here. Edit the card or mark it inactive instead.");
                SetErrors(true);
                UpdateTabButtons();
                return;
            }

            if (ReferenceEquals(_editingRow, row))
                ClearEntryFields();

            row.Wipe();
            _bankCardRows.Remove(row);

            SetErrors(false);
            MarkDirty();
            UpdateTabButtons();
        }

        // ============================================================
        // Tab-level Save/Cancel
        // ============================================================

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
            ClearNewCardSessionTracking("OnTabCancelClick");
            ClearBankCardError();

            HideRevealsAndStopTimer(clearRevealOverlays: true);
            WipeSensitiveEntryFields();
            ResetBankCardFieldBackgrounds();
            ClearSelectedBankCardDetailSedsBestEffort();
            _isSelectedProtectedViewActive = false;

            CancelAndExitRequested?.Invoke(this, EventArgs.Empty);
        }

        private void RaiseSaveAndExitRequestOnly()
        {
            var payload = _bankCardRows.Select(CloneRow).ToList();
            SaveAndExitRequested?.Invoke(this, new BankCardsCommitEventArgs(payload));
        }

        // ============================================================
        // Validation
        // ============================================================

        private bool ValidateTabStateOnly(bool showErrors)
        {
            if (EntryLineHasAnyInput())
            {
                if (showErrors) ShowBankCardError("Finish Add/Update or Clear Row before saving.");
                return false;
            }

            foreach (var row in _bankCardRows)
            {
                // For service-loaded rows, raw values are empty. That is OK at panel-level.
                // Panel validation focuses on format of user-entered rows (Id==0) and updates.
                if (row.Id == 0)
                {
                    if (!TryValidateCardNumber(row.CardNumberRaw ?? string.Empty, out var cardErr))
                    {
                        if (showErrors) ShowBankCardError($"Invalid card number in grid: {cardErr}");
                        return false;
                    }
                }

                if (!TryValidateExpiration(row.Expiration ?? string.Empty, out _, out _, out var expErr))
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

        private bool ValidateBankCardFields(bool showErrors, out int expMonth, out int expYear, out string expNormalized)
        {
            expMonth = 0;
            expYear = 0;
            expNormalized = string.Empty;

            if (showErrors)
                ResetBankCardFieldBackgrounds();

            var selection = CardTypeCombo?.SelectedItem as CardTypeItem;
            string cardNumber = GetCurrentCardNumber();
            string customCardName = GetCurrentCustomCardName();
            string expiration = (ExpirationTextBox?.Text ?? "").Trim();

            string cvv = GetCurrentCvv();
            string pin = GetCurrentPin();

            if (selection == null)
            {
                if (showErrors) ShowBankCardError("Please choose a card type.", CardTypeCombo);
                return false;
            }

            if (IsFreeformCardType(selection) && string.IsNullOrWhiteSpace(customCardName))
            {
                if (showErrors) ShowBankCardError("Custom card name is required for FREEFORM.", CustomCardNameTextBox);
                return false;
            }

            if (!TryValidateCardNumber(cardNumber, out string cardError))
            {
                if (showErrors) ShowBankCardError(cardError, CardNumberBox);
                return false;
            }

            if (!TryValidateExpiration(expiration, out expMonth, out expYear, out string expError))
            {
                if (showErrors) ShowBankCardError(expError, ExpirationTextBox);
                return false;
            }

            expNormalized = $"{expMonth:00}/{expYear:0000}";

            if (!TryValidateCvv(cvv, out string cvvError))
            {
                if (showErrors) ShowBankCardError(cvvError, CvvBox);
                return false;
            }

            if (!TryValidateCardPin(pin, out string pinError))
            {
                if (showErrors) ShowBankCardError(pinError, PinBox);
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

        private static bool TryValidateExpiration(string input, out int month, out int year, out string errorMessage)
        {
            month = 0;
            year = 0;
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

            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out month) || month < 1 || month > 12)
            {
                errorMessage = "Expiration month must be between 01 and 12.";
                return false;
            }

            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out year))
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

            int lastDay = DateTime.DaysInMonth(year, month);
            var expDate = new DateTime(year, month, lastDay);

            if (expDate < DateTime.Today)
            {
                errorMessage = "Expiration date must be this month or later.";
                return false;
            }

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
            CustomCardNameTextBox?.ClearValue(BackgroundProperty);

            CardNumberBox?.ClearValue(BackgroundProperty);
            CardNumberPlainTextBox?.ClearValue(BackgroundProperty);

            ExpirationTextBox?.ClearValue(BackgroundProperty);

            CvvBox?.ClearValue(BackgroundProperty);
            CvvPlainTextBox?.ClearValue(BackgroundProperty);

            PinBox?.ClearValue(BackgroundProperty);
            PinPlainTextBox?.ClearValue(BackgroundProperty);
        }

        // ============================================================
        // Entry lifecycle / wipe
        // ============================================================

        private void WipeSensitiveEntryFields()
        {
            HideRevealsAndStopTimer(clearRevealOverlays: true);

            if (CardNumberBox != null) UICleaner.Clear(CardNumberBox);
            if (CvvBox != null) UICleaner.Clear(CvvBox);
            if (PinBox != null) UICleaner.Clear(PinBox);

            ClearRevealOverlayTextOnly(CardNumberPlainTextBox);
            ClearRevealOverlayTextOnly(CvvPlainTextBox);
            ClearRevealOverlayTextOnly(PinPlainTextBox);

            if (CustomCardNameTextBox != null) UICleaner.Clear(CustomCardNameTextBox);
            if (ExpirationTextBox != null) UICleaner.Clear(ExpirationTextBox);
        }

        private void ClearEntryFields()
        {
            _suppressDirty = true;
            try
            {
                if (CardTypeCombo != null)
                    CardTypeCombo.SelectedIndex = _cardTypeItems.Count > 0 ? 0 : -1;

                if (CustomCardNameTextBox != null) UICleaner.Clear(CustomCardNameTextBox);
                if (CardNumberBox != null) UICleaner.Clear(CardNumberBox);
                HideCardNumber(clearOverlay: true);

                if (ExpirationTextBox != null) UICleaner.Clear(ExpirationTextBox);

                if (CvvBox != null) UICleaner.Clear(CvvBox);
                HideCvv(clearOverlay: true);

                if (PinBox != null) UICleaner.Clear(PinBox);
                HidePin(clearOverlay: true);

                if (ChkCardActive != null)
                    ChkCardActive.IsChecked = true;

                _isSelectedProtectedViewActive = false;
                SetEditingMode(null);
                UpdateCustomCardNameUi(clearWhenHidden: true);

                ClearBankCardError();
                ResetBankCardFieldBackgrounds();
            }
            finally
            {
                _suppressDirty = false;
            }
        }

        private void TeardownAfterSuccessfulUpdate()
        {
            ClearEntryFields();
            ClearSelectedBankCardDetailSedsBestEffort();
            _isSelectedProtectedViewActive = false;
            if (BankCardGrid != null)
                BankCardGrid.SelectedItem = null;
            SetEditingMode(null);
        }
        private void WipeAndClearBankCardRows()
        {
            foreach (var row in _bankCardRows.ToList())
                row.Wipe();

            _bankCardRows.Clear();
        }

        // ============================================================
        // Helpers
        // ============================================================

        private void SetEditingMode(BankCardRow? row)
        {
            _editingRow = row;

            if (BtnBankCardAddOrUpdate != null)
                BtnBankCardAddOrUpdate.Content = (_editingRow == null) ? "Add" : "Update";
        }

        private bool EntryLineHasAnyInput()
        {
            if (_isSelectedProtectedViewActive)
                return false;

            if (CardNumberBox != null && !string.IsNullOrWhiteSpace(CardNumberBox.Password))
                return true;

            if (ExpirationTextBox != null && !string.IsNullOrWhiteSpace(ExpirationTextBox.Text))
                return true;

            if (CustomCardNameTextBox != null && !string.IsNullOrWhiteSpace(CustomCardNameTextBox.Text))
                return true;

            if (CvvBox != null && !string.IsNullOrWhiteSpace(CvvBox.Password))
                return true;

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

        private string GetCurrentCvv()
        {
            return CvvBox?.Password ?? string.Empty;
        }

        private string GetCurrentPin()
        {
            return PinBox?.Password ?? string.Empty;
        }

        public void BeginAddNewCardSession()
        {
            if (_entryDisabled || _isSelectedProtectedViewActive || _editingRow != null)
                return;

            if (_newCardSessionStarted)
                return;

            _newCardSessionStarted = true;

        }

        private bool HasPendingBlankAddAttempt()
        {
            return _newCardSessionStarted &&
                   !_hasChanges &&
                   !EntryLineHasAnyInput() &&
                   _editingRow == null;
        }

        private void ClearNewCardSessionTracking(string source)
        {
            if (!_newCardSessionStarted)
                return;

            _newCardSessionStarted = false;

        }

        private static string TrimToMaxChars(string? value)
        {
            var s = value ?? string.Empty;
            return s.Length <= MaxCardNumberChars ? s : s.Substring(0, MaxCardNumberChars);
        }

        private static string MaskPanLast4(string digits)
        {
            if (string.IsNullOrWhiteSpace(digits))
                return string.Empty;

            var d = digits.Trim();
            if (d.Length <= 4)
                return $"•••• {d}";

            string last4 = d.Substring(d.Length - 4, 4);
            return $"•••• {last4}";
        }

        // ============================================================
        // Wire/unwire events
        // ============================================================

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

        // ============================================================
        // DTOs
        // ============================================================

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
            public long Id { get; set; }

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
                set
                {
                    if (_cardTypeDisplay != value)
                    {
                        _cardTypeDisplay = value ?? string.Empty;
                        OnPropertyChanged(nameof(CardTypeDisplay));
                        OnPropertyChanged(nameof(DisplayCardTypeText));
                    }
                }
            }

            private string _cardholder = string.Empty;
            public string Cardholder
            {
                get => _cardholder;
                set
                {
                    if (_cardholder != value)
                    {
                        _cardholder = value ?? string.Empty;
                        OnPropertyChanged(nameof(Cardholder));
                        OnPropertyChanged(nameof(DisplayCardTypeText));
                    }
                }
            }

            private bool _isFreeform;
            public bool IsFreeform
            {
                get => _isFreeform;
                set
                {
                    if (_isFreeform != value)
                    {
                        _isFreeform = value;
                        OnPropertyChanged(nameof(IsFreeform));
                        OnPropertyChanged(nameof(DisplayCardTypeText));
                    }
                }
            }

            public string DisplayCardTypeText =>
                IsFreeform && !string.IsNullOrWhiteSpace(Cardholder)
                    ? Cardholder
                    : CardTypeDisplay;

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
                    }
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
                set { if (_cvvRaw != value) { _cvvRaw = value ?? string.Empty; OnPropertyChanged(nameof(CvvRaw)); } }
            }

            private string _pinRaw = string.Empty;
            public string PinRaw
            {
                get => _pinRaw;
                set { if (_pinRaw != value) { _pinRaw = value ?? string.Empty; OnPropertyChanged(nameof(PinRaw)); } }
            }

            private bool _isActive = true;
            public bool IsActive
            {
                get => _isActive;
                set { if (_isActive != value) { _isActive = value; OnPropertyChanged(nameof(IsActive)); } }
            }

            private string _cardNumberMasked = string.Empty;
            public string CardNumberMasked
            {
                get => _cardNumberMasked;
                set { if (_cardNumberMasked != value) { _cardNumberMasked = value ?? string.Empty; OnPropertyChanged(nameof(CardNumberMasked)); } }
            }

            private string _cvvMasked = string.Empty;
            public string CvvMasked
            {
                get => _cvvMasked;
                set { if (_cvvMasked != value) { _cvvMasked = value ?? string.Empty; OnPropertyChanged(nameof(CvvMasked)); } }
            }

            private string _pinMasked = string.Empty;
            public string PinMasked
            {
                get => _pinMasked;
                set { if (_pinMasked != value) { _pinMasked = value ?? string.Empty; OnPropertyChanged(nameof(PinMasked)); } }
            }

            public void Wipe()
            {
                CardTypeDisplay = string.Empty;
                CardTypeId = 0;
                Cardholder = string.Empty;
                IsFreeform = false;
                CardNumberRaw = string.Empty;
                Expiration = string.Empty;
                CvvRaw = string.Empty;
                PinRaw = string.Empty;

                CardNumberMasked = string.Empty;
                CvvMasked = string.Empty;
                PinMasked = string.Empty;

                IsActive = false;
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged(string propertyName) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
