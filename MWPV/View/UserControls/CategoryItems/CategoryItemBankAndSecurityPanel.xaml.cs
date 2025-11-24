using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MWPV.Services;
namespace MWPV.View.UserControls.CategoryItems
{
    public partial class CategoryItemBankAndSecurityPanel : UserControl
    {
        // Public model we can use later from the parent
        public sealed class BankCardRow
        {
            public string CardTypeCode { get; set; } = string.Empty;
            public string CardTypeDisplay { get; set; } = string.Empty;
            public string CardNumberMasked { get; set; } = string.Empty;
            public string ExpirationDisplay { get; set; } = string.Empty;
            public string CvvMasked { get; set; } = string.Empty;
            public string PinMasked { get; set; } = string.Empty;
            public bool IsActive { get; set; }
        }

        public sealed class AccountInfo
        {
            public string? AccountName { get; set; }
            public string? AccountNumber { get; set; }
            public string? AccountPin { get; set; }
        }

        public sealed class SecurityQuestionRow
        {
            public int Seq { get; set; }
            public string Question { get; set; } = string.Empty;
            public string Answer { get; set; } = string.Empty;
        }

        public sealed class CategoryItemBankSectionModel
        {
            public List<BankCardRow> BankCards { get; } = new();
            public AccountInfo Account { get; } = new();
            public List<SecurityQuestionRow> SecurityQuestions { get; } = new();
        }

        // --- bank card combos ------------------------------------------------

        // ComboTypeId for bank cards; mapped to ComboDetail.ComboTyp = 2
        private const int BankCardComboTypeId = 2;

        private bool _bankCombosLoaded;
        private readonly List<CardTypeItem> _cardTypeItems = new();

        private sealed class CardTypeItem
        {
            public string Code { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
        }

        // In-memory bank card list for the ItemsControl
        private readonly ObservableCollection<BankCardRow> _bankCards = new();
        private BankCardRow? _editingCard;

        // Reveal state + timer (local to this panel)
        private readonly DispatcherTimer _revealTimer;
        private bool _cvvRevealed;
        private bool _cardPinRevealed;

        // Security Q/A (UI-only)
        private readonly ObservableCollection<QaRow> _qaRows = new();
        private const string QA_BTN_VIEW = "View";
        private const string QA_BTN_DELETE = "Delete";
        private const string QA_BTN_UP = "Up";
        private const string QA_BTN_DOWN = "Down";

        private sealed class QaRow
        {
            public int Seq { get; set; }
            public string QuestionDisplay { get; set; } = string.Empty;
            public int AnswerLen { get; set; }
            public string AnswerDisplay { get; set; } = string.Empty;
        }

        public CategoryItemBankAndSecurityPanel()
        {
            InitializeComponent();

            Loaded += CategoryItemBankAndSecurityPanel_Loaded;
            Unloaded += CategoryItemBankAndSecurityPanel_Unloaded;

            _revealTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(20)
            };
            _revealTimer.Tick += RevealTimer_Tick;
        }

        private void CategoryItemBankAndSecurityPanel_Loaded(object? sender, RoutedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[BANK-PANEL] Loaded");
#endif
            if (!_bankCombosLoaded)
            {
                LoadBankCardCombos();
                _bankCombosLoaded = true;
            }

            icBankCards.ItemsSource = _bankCards;
            UpdateBankCardsPlaceholder();

            InitSecurityQaUi();

            ClearAll();
            HideAllRevealsAndStopTimer();
        }

        private void CategoryItemBankAndSecurityPanel_Unloaded(object? sender, RoutedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[BANK-PANEL] Unloaded");
#endif
            HideAllRevealsAndStopTimer();
            ClearAll();

            var list = qaList;
            if (list is not null)
            {
                list.RemoveHandler(Button.ClickEvent, new RoutedEventHandler(QaList_ButtonClick));
            }
        }

        // ====================== Public API (for parent) ======================

        public void ClearAll()
        {
            try
            {
                // Entry line
                cboCardType.SelectedIndex = -1;
                txtCardNumber.Text = string.Empty;
                txtExpDate.Text = string.Empty;
                txtCvv.Password = string.Empty;
                txtCvvPlain.Text = string.Empty;
                txtCardPin.Password = string.Empty;
                txtCardPinPlain.Text = string.Empty;
                chkCardActive.IsChecked = true;

                // Lists
                _editingCard = null;
                _bankCards.Clear();
                UpdateBankCardsPlaceholder();

                // Account
                cboAccountName.SelectedIndex = -1;
                txtAccountNumber.Text = string.Empty;
                txtAccountPin.Password = string.Empty;

                // Security Q/A
                _qaRows.Clear();
                UpdateQaCountText();
            }
            catch
            {
            }
        }

        public CategoryItemBankSectionModel ExportToModel()
        {
            var model = new CategoryItemBankSectionModel();

            foreach (var c in _bankCards)
            {
                model.BankCards.Add(new BankCardRow
                {
                    CardTypeCode = c.CardTypeCode,
                    CardTypeDisplay = c.CardTypeDisplay,
                    CardNumberMasked = c.CardNumberMasked,
                    ExpirationDisplay = c.ExpirationDisplay,
                    CvvMasked = c.CvvMasked,
                    PinMasked = c.PinMasked,
                    IsActive = c.IsActive
                });
            }

            model.Account.AccountName = cboAccountName.Text;
            model.Account.AccountNumber = txtAccountNumber.Text;
            model.Account.AccountPin = txtAccountPin.Password;

            foreach (var r in _qaRows)
            {
                model.SecurityQuestions.Add(new SecurityQuestionRow
                {
                    Seq = r.Seq,
                    Question = r.QuestionDisplay,
                    Answer = string.Empty // we are not persisting answers yet
                });
            }

            return model;
        }

        public void LoadFromModel(CategoryItemBankSectionModel? model)
        {
            ClearAll();
            if (model is null) return;

            // Bank cards
            _bankCards.Clear();
            foreach (var c in model.BankCards)
            {
                _bankCards.Add(new BankCardRow
                {
                    CardTypeCode = c.CardTypeCode,
                    CardTypeDisplay = c.CardTypeDisplay,
                    CardNumberMasked = c.CardNumberMasked,
                    ExpirationDisplay = c.ExpirationDisplay,
                    CvvMasked = c.CvvMasked,
                    PinMasked = c.PinMasked,
                    IsActive = c.IsActive
                });
            }
            UpdateBankCardsPlaceholder();

            // Account
            if (!string.IsNullOrWhiteSpace(model.Account.AccountName))
                cboAccountName.Text = model.Account.AccountName;
            txtAccountNumber.Text = model.Account.AccountNumber ?? string.Empty;
            txtAccountPin.Password = model.Account.AccountPin ?? string.Empty;

            // Security Q/A (we only restore what we currently store: question + length display)
            _qaRows.Clear();
            foreach (var q in model.SecurityQuestions.OrderBy(q => q.Seq))
            {
                var row = new QaRow
                {
                    Seq = q.Seq,
                    QuestionDisplay = q.Question ?? string.Empty,
                    AnswerLen = 0,
                    AnswerDisplay = "(hidden)"
                };
                _qaRows.Add(row);
            }
            UpdateQaCountText();
        }

        public IReadOnlyList<BankCardRow> GetBankCards() => _bankCards.ToList();

        // ======================= Bank card combo load ========================

        private void LoadBankCardCombos()
        {
            try
            {
#if DEBUG
                Debug.WriteLine($"[BANK-PANEL][BANK-CARD] Loading card types for ComboTypeId='{BankCardComboTypeId}'");
#endif
                _cardTypeItems.Clear();

                var dbTypes = ComboDetailService.GetByTypeId(BankCardComboTypeId);

                foreach (var t in dbTypes.OrderBy(t => t.Seq))
                {
                    if (string.IsNullOrWhiteSpace(t.Code))
                        continue;

                    _cardTypeItems.Add(new CardTypeItem
                    {
                        Code = t.Code,
                        Description = string.IsNullOrWhiteSpace(t.Description)
                            ? t.Code
                            : t.Description
                    });
                }

                cboCardType.ItemsSource = _cardTypeItems;
                cboCardType.DisplayMemberPath = nameof(CardTypeItem.Description);
                cboCardType.SelectedValuePath = nameof(CardTypeItem.Code);

#if DEBUG
                Debug.WriteLine($"[BANK-PANEL][BANK-CARD] Loaded {_cardTypeItems.Count} card types.");
#endif
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine($"[BANK-PANEL][BANK-CARD][FAIL] {ex}");
#endif
                MessageBox.Show(
                    "Unable to load bank card types. You can still type card details manually.\n\n" + ex.Message,
                    "Bank Cards",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        // ==================== Shared reveal timer (local) ====================

        private void RevealTimer_Tick(object? sender, EventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[BANK-PANEL] Reveal timer elapsed – hiding card fields");
#endif
            HideAllRevealsAndStopTimer();
        }

        private void HideAllRevealsAndStopTimer()
        {
            _revealTimer.Stop();
            HideCvv();
            HideCardPin();
        }

        private void StartOrRestartRevealTimerIfNeeded()
        {
            _revealTimer.Stop();

            if (_cvvRevealed || _cardPinRevealed)
            {
                _revealTimer.Start();
            }
        }

        // ======================= CVV / Card PIN reveal =======================

        private void txtCvv_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_cvvRevealed)
                txtCvvPlain.Text = txtCvv.Password;

            if (_cvvRevealed || _cardPinRevealed)
                StartOrRestartRevealTimerIfNeeded();
        }

        private void txtCardPin_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_cardPinRevealed)
                txtCardPinPlain.Text = txtCardPin.Password;

            if (_cvvRevealed || _cardPinRevealed)
                StartOrRestartRevealTimerIfNeeded();
        }

        private void BtnToggleCvvReveal_Click(object? sender, RoutedEventArgs e)
        {
            if (_cvvRevealed)
                HideCvv();
            else
                ShowCvv();

            StartOrRestartRevealTimerIfNeeded();
        }

        private void BtnToggleCardPinReveal_Click(object? sender, RoutedEventArgs e)
        {
            if (_cardPinRevealed)
                HideCardPin();
            else
                ShowCardPin();

            StartOrRestartRevealTimerIfNeeded();
        }

        private void ShowCvv()
        {
            txtCvvPlain.Text = txtCvv.Password;
            txtCvvPlain.Visibility = Visibility.Visible;
            txtCvv.Visibility = Visibility.Collapsed;
            _cvvRevealed = true;
        }

        private void HideCvv()
        {
            if (!string.IsNullOrEmpty(txtCvvPlain.Text))
                txtCvvPlain.Text = string.Empty;

            txtCvvPlain.Visibility = Visibility.Collapsed;
            txtCvv.Visibility = Visibility.Visible;
            _cvvRevealed = false;
        }

        private void ShowCardPin()
        {
            txtCardPinPlain.Text = txtCardPin.Password;
            txtCardPinPlain.Visibility = Visibility.Visible;
            txtCardPin.Visibility = Visibility.Collapsed;
            _cardPinRevealed = true;
        }

        private void HideCardPin()
        {
            if (!string.IsNullOrEmpty(txtCardPinPlain.Text))
                txtCardPinPlain.Text = string.Empty;

            txtCardPinPlain.Visibility = Visibility.Collapsed;
            txtCardPin.Visibility = Visibility.Visible;
            _cardPinRevealed = false;
        }

        // ================== Bank card entry line buttons =====================

        private void btnBankCardAdd_Click(object? sender, RoutedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[BANK-PANEL][BANK-CARD] Add clicked");
#endif
            var typeItem = cboCardType.SelectedItem as CardTypeItem;
            string typeCode = typeItem?.Code ?? string.Empty;
            string typeDesc = typeItem?.Description ?? string.Empty;

            string cardNumber = (txtCardNumber.Text ?? string.Empty).Trim();
            string exp = (txtExpDate.Text ?? string.Empty).Trim();
            string cvv = txtCvv.Password ?? string.Empty;
            string pin = txtCardPin.Password ?? string.Empty;
            bool isActive = chkCardActive.IsChecked == true;

            if (string.IsNullOrWhiteSpace(cardNumber))
            {
                MessageBox.Show("Please enter a card number (or skip this card).",
                                "Bank Cards",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                txtCardNumber.Focus();
                return;
            }

            // Very simple masking for now – we only display masked text
            string maskedCard =
                cardNumber.Length <= 4
                    ? cardNumber
                    : new string('•', Math.Max(0, cardNumber.Length - 4)) + cardNumber[^4..];

            string maskedCvv = string.IsNullOrEmpty(cvv) ? string.Empty : new string('•', cvv.Length);
            string maskedPin = string.IsNullOrEmpty(pin) ? string.Empty : new string('•', pin.Length);

            if (_editingCard is null)
            {
                var row = new BankCardRow
                {
                    CardTypeCode = typeCode,
                    CardTypeDisplay = string.IsNullOrWhiteSpace(typeDesc) ? typeCode : typeDesc,
                    CardNumberMasked = maskedCard,
                    ExpirationDisplay = exp,
                    CvvMasked = maskedCvv,
                    PinMasked = maskedPin,
                    IsActive = isActive
                };
                _bankCards.Add(row);
            }
            else
            {
                _editingCard.CardTypeCode = typeCode;
                _editingCard.CardTypeDisplay = string.IsNullOrWhiteSpace(typeDesc) ? typeCode : typeDesc;
                _editingCard.CardNumberMasked = maskedCard;
                _editingCard.ExpirationDisplay = exp;
                _editingCard.CvvMasked = maskedCvv;
                _editingCard.PinMasked = maskedPin;
                _editingCard.IsActive = isActive;
            }

            _editingCard = null;
            icBankCards.Items.Refresh();
            UpdateBankCardsPlaceholder();
            ClearBankEntryLine();
        }

        private void btnBankCardClear_Click(object? sender, RoutedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[BANK-PANEL][BANK-CARD] Clear clicked");
#endif
            _editingCard = null;
            ClearBankEntryLine();
        }

        private void ClearBankEntryLine()
        {
            cboCardType.SelectedIndex = -1;
            txtCardNumber.Text = string.Empty;
            txtExpDate.Text = string.Empty;
            txtCvv.Password = string.Empty;
            txtCvvPlain.Text = string.Empty;
            txtCardPin.Password = string.Empty;
            txtCardPinPlain.Text = string.Empty;
            chkCardActive.IsChecked = true;

            HideAllRevealsAndStopTimer();
        }

        private void btnBankCardRowEdit_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            if (!TryGetDataContext<BankCardRow>(btn, out var row) || row is null) return;

#if DEBUG
            Debug.WriteLine("[BANK-PANEL][BANK-CARD] Row Edit clicked");
#endif
            _editingCard = row;

            // Best-effort restore – we only have masked values right now
            if (!string.IsNullOrEmpty(row.CardTypeCode))
            {
                cboCardType.SelectedValue = row.CardTypeCode;
            }
            else
            {
                cboCardType.SelectedIndex = -1;
            }

            txtCardNumber.Text = row.CardNumberMasked;
            txtExpDate.Text = row.ExpirationDisplay;
            txtCvv.Password = row.CvvMasked.Replace("•", "");
            txtCardPin.Password = row.PinMasked.Replace("•", "");
            chkCardActive.IsChecked = row.IsActive;

            HideAllRevealsAndStopTimer();
        }

        private void btnBankCardRowDelete_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            if (!TryGetDataContext<BankCardRow>(btn, out var row) || row is null) return;

#if DEBUG
            Debug.WriteLine("[BANK-PANEL][BANK-CARD] Row Delete clicked");
#endif
            if (_editingCard == row)
                _editingCard = null;

            _bankCards.Remove(row);
            UpdateBankCardsPlaceholder();
        }

        private void UpdateBankCardsPlaceholder()
        {
            if (txtNoBankCardsPlaceholder is null) return;

            txtNoBankCardsPlaceholder.Visibility =
                _bankCards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // --------------- Security Q/A helpers (UI-only for now) ---------------

        private void InitSecurityQaUi()
        {
            var list = qaList;
            if (list is not null)
            {
                list.ItemsSource = _qaRows;
                list.AddHandler(Button.ClickEvent,
                                new RoutedEventHandler(QaList_ButtonClick),
                                handledEventsToo: true);
            }

            var addBtn = qaAddButton;
            if (addBtn is not null)
                addBtn.Click += QaAddButton_Click;

            UpdateQaCountText();
        }

        private void QaAddButton_Click(object? sender, RoutedEventArgs e)
        {
            var qBox = qaNewQuestion;
            var aBox = qaNewAnswer;

            string q = (qBox?.Text ?? string.Empty).Trim();
            string a = (aBox?.Password ?? string.Empty);

            if (string.IsNullOrWhiteSpace(q))
            {
                MessageBox.Show("Please enter a security question.",
                                "Validation",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                qBox?.Focus();
                return;
            }

            int len = a?.Length ?? 0;

            var row = new QaRow
            {
                Seq = _qaRows.Count,
                QuestionDisplay = q,
                AnswerLen = len,
                AnswerDisplay = len > 0 ? $"•••• ({len})" : "(empty)"
            };

            _qaRows.Add(row);

            if (qBox is not null) qBox.Text = string.Empty;
            if (aBox is not null) aBox.Password = string.Empty;

            UpdateQaCountText();
        }

        private void QaList_ButtonClick(object? sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is not Button btn) return;
            if (!TryGetDataContext<QaRow>(btn, out var row) || row is null) return;

            string label = btn.Content?.ToString() ?? string.Empty;

            if (label == QA_BTN_VIEW)
            {
                MessageBox.Show(
                    "Viewing answers will be enabled once encryption is wired.\n\n(We are not storing plaintext in memory at this stage.)",
                    "Not Implemented",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else if (label == QA_BTN_DELETE)
            {
                _qaRows.Remove(row);
                ResequenceQa();
                UpdateQaCountText();
            }
            else if (label == QA_BTN_UP)
            {
                var idx = _qaRows.IndexOf(row);
                if (idx > 0)
                {
                    _qaRows.Move(idx, idx - 1);
                    ResequenceQa();
                }
            }
            else if (label == QA_BTN_DOWN)
            {
                var idx = _qaRows.IndexOf(row);
                if (idx >= 0 && idx < _qaRows.Count - 1)
                {
                    _qaRows.Move(idx, idx + 1);
                    ResequenceQa();
                }
            }
        }

        private void UpdateQaCountText()
        {
            if (QaCountText is null) return;
            QaCountText.Text = $"— {_qaRows.Count}";
        }

        private void ResequenceQa()
        {
            for (int i = 0; i < _qaRows.Count; i++)
                _qaRows[i].Seq = i;
        }

        // ---------------------------- helpers --------------------------------

        private static bool TryGetDataContext<T>(DependencyObject start, out T? ctx)
            where T : class
        {
            ctx = null;
            DependencyObject? current = start;
            while (current is not null)
            {
                if (current is FrameworkElement fe &&
                    fe.DataContext is T match)
                {
                    ctx = match;
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }
    }
}
