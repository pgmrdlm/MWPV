using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MWPV.Services;

namespace MWPV.View.UserControls.CategoryItems
{
    public partial class CategoryItemBankCardsPanel : UserControl
    {
        // Exposed collections (for binding / parent access)
        private readonly ObservableCollection<BankCardRow> _bankCardRows = new();
        private readonly ObservableCollection<CardTypeItem> _cardTypeItems = new();

        private BankCardRow? _editingRow;

        // Reveal state for entry line fields
        private bool _isCardNumberRevealed;
        private bool _isCvvRevealed;
        private bool _isPinRevealed;

        public ObservableCollection<BankCardRow> BankCardRows => _bankCardRows;
        public ObservableCollection<CardTypeItem> CardTypeItems => _cardTypeItems;

        public CategoryItemBankCardsPanel()
        {
            InitializeComponent();

            DataContext = this;

            Debug.WriteLine("[BANK-CARDS-PANEL] Loaded");

            LoadBankCardTypes();

            _isCardNumberRevealed = false;
            _isCvvRevealed = false;
            _isPinRevealed = false;

            UpdateCardNumberRevealState();
            UpdateCvvRevealState();
            UpdatePinRevealState();
        }

        // ====================================================================
        // Public helpers (for host control, if needed later)
        // ====================================================================

        public void ClearAll()
        {
            ClearEntryFields();
            _bankCardRows.Clear();
            ClearBankCardError();
        }

        // ====================================================================
        // Load combos
        // ====================================================================

        private void LoadBankCardTypes()
        {
            try
            {
                const int comboTypeId = 2; // bank card types
                Debug.WriteLine($"[BANK-CARDS-PANEL] Loading card types for ComboTypeId='{comboTypeId}'");

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
                        Description = string.IsNullOrWhiteSpace(t.Description)
                            ? t.Code
                            : t.Description
                    });
                }

                Debug.WriteLine($"[BANK-CARDS-PANEL] Loaded {_cardTypeItems.Count} card types.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BANK-CARDS-PANEL][ERROR] {ex}");
                MessageBox.Show(
                    "Unable to load bank card types. Bank card entry will be disabled for this session.",
                    "Bank Cards",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // ====================================================================
        // Card number / CVV / PIN reveal handlers (entry line)
        // ====================================================================

        private void CardNumberBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            // When masked, keep the plain text copy in sync for later reveal.
            if (!_isCardNumberRevealed)
            {
                CardNumberPlainTextBox.Text = CardNumberBox.Password;
            }
        }

        private void CvvBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (!_isCvvRevealed)
            {
                CvvPlainTextBox.Text = CvvBox.Password;
            }
        }

        private void BtnToggleCvvReveal_Click(object sender, RoutedEventArgs e)
        {
            _isCvvRevealed = !_isCvvRevealed;
            UpdateCvvRevealState();
        }

        private void PinBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (!_isPinRevealed)
            {
                PinPlainTextBox.Text = PinBox.Password;
            }
        }

        private void BtnTogglePinReveal_Click(object sender, RoutedEventArgs e)
        {
            _isPinRevealed = !_isPinRevealed;
            UpdatePinRevealState();
        }

        private void UpdateCardNumberRevealState()
        {
            if (CardNumberBox == null || CardNumberPlainTextBox == null || BtnViewCardNumber == null)
                return;

            if (_isCardNumberRevealed)
            {
                // Show plain text, hide PasswordBox
                CardNumberPlainTextBox.Text = CardNumberBox.Password;
                CardNumberPlainTextBox.Visibility = Visibility.Visible;
                CardNumberPlainTextBox.IsHitTestVisible = true;

                CardNumberBox.Visibility = Visibility.Collapsed;
                CardNumberBox.IsEnabled = false;

                BtnViewCardNumber.ToolTip = "Hide card number";
            }
            else
            {
                // Show PasswordBox, hide plain text
                CardNumberBox.Password = CardNumberPlainTextBox.Text;
                CardNumberBox.Visibility = Visibility.Visible;
                CardNumberBox.IsEnabled = true;

                CardNumberPlainTextBox.Visibility = Visibility.Collapsed;
                CardNumberPlainTextBox.IsHitTestVisible = false;

                BtnViewCardNumber.ToolTip = "Show card number";
            }
        }

        private void UpdateCvvRevealState()
        {
            if (CvvBox == null || CvvPlainTextBox == null || BtnToggleCvvReveal == null)
                return;

            if (_isCvvRevealed)
            {
                CvvPlainTextBox.Text = CvvBox.Password;
                CvvPlainTextBox.Visibility = Visibility.Visible;
                CvvPlainTextBox.IsHitTestVisible = true;

                CvvBox.Visibility = Visibility.Collapsed;
                CvvBox.IsEnabled = false;

                BtnToggleCvvReveal.ToolTip = "Hide CVV";
            }
            else
            {
                CvvBox.Password = CvvPlainTextBox.Text;
                CvvBox.Visibility = Visibility.Visible;
                CvvBox.IsEnabled = true;

                CvvPlainTextBox.Visibility = Visibility.Collapsed;
                CvvPlainTextBox.IsHitTestVisible = false;

                BtnToggleCvvReveal.ToolTip = "Show CVV";
            }
        }

        private void UpdatePinRevealState()
        {
            if (PinBox == null || PinPlainTextBox == null || BtnTogglePinReveal == null)
                return;

            if (_isPinRevealed)
            {
                PinPlainTextBox.Text = PinBox.Password;
                PinPlainTextBox.Visibility = Visibility.Visible;
                PinPlainTextBox.IsHitTestVisible = true;

                PinBox.Visibility = Visibility.Collapsed;
                PinBox.IsEnabled = false;

                BtnTogglePinReveal.ToolTip = "Hide card PIN";
            }
            else
            {
                PinBox.Password = PinPlainTextBox.Text;
                PinBox.Visibility = Visibility.Visible;
                PinBox.IsEnabled = true;

                PinPlainTextBox.Visibility = Visibility.Collapsed;
                PinPlainTextBox.IsHitTestVisible = false;

                BtnTogglePinReveal.ToolTip = "Show card PIN";
            }
        }

        // Helper to read the current card number from whichever control is active
        private string GetCurrentCardNumber()
        {
            string value = _isCardNumberRevealed
                ? CardNumberPlainTextBox.Text
                : CardNumberBox.Password;

            return (value ?? string.Empty).Trim();
        }

        // ====================================================================
        // Button handlers
        // ====================================================================

        private void OnBankCardAddOrUpdateClick(object sender, RoutedEventArgs e)
        {
            ClearBankCardError();

            if (!ValidateBankCardFields(showErrors: true))
            {
                return;
            }

            var selection = CardTypeCombo.SelectedItem as CardTypeItem;
            if (selection == null)
            {
                ShowBankCardError("Please choose a card type.", CardTypeCombo);
                return;
            }

            string cardNumber = GetCurrentCardNumber();
            string expirationInput = (ExpirationTextBox.Text ?? "").Trim();

            string cvv = _isCvvRevealed
                ? (CvvPlainTextBox.Text ?? string.Empty)
                : (CvvBox.Password ?? string.Empty);
            string pin = _isPinRevealed
                ? (PinPlainTextBox.Text ?? string.Empty)
                : (PinBox.Password ?? string.Empty);

            bool isActive = ChkCardActive.IsChecked == true;

            if (!TryValidateExpiration(expirationInput, out string expNormalized, out _))
            {
                ShowBankCardError("Expiration date is invalid.", ExpirationTextBox);
                return;
            }

            if (_editingRow == null)
            {
                bool duplicateType = _bankCardRows.Any(r => r.CardTypeId == selection.ComboDetailId);
                if (duplicateType)
                {
                    ShowBankCardError("Only one card of each type is allowed.", CardTypeCombo);
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
        }

        private void OnBankCardClearClick(object sender, RoutedEventArgs e)
        {
            ClearBankCardError();
            ClearEntryFields();
        }

        private void OnBankCardFieldLostFocus(object sender, RoutedEventArgs e)
        {
            ValidateBankCardFields(showErrors: true);
        }

        private void OnBankCardEditClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not BankCardRow row)
                return;

            _editingRow = row;

            var cardType = _cardTypeItems.FirstOrDefault(ct => ct.ComboDetailId == row.CardTypeId);
            if (cardType != null)
            {
                CardTypeCombo.SelectedItem = cardType;
            }

            CardNumberBox.Password = row.CardNumberRaw;
            CardNumberPlainTextBox.Text = row.CardNumberRaw;
            _isCardNumberRevealed = false;
            UpdateCardNumberRevealState();

            ExpirationTextBox.Text = row.Expiration;

            CvvBox.Password = row.CvvRaw;
            CvvPlainTextBox.Text = row.CvvRaw;
            _isCvvRevealed = false;
            UpdateCvvRevealState();

            PinBox.Password = row.PinRaw;
            PinPlainTextBox.Text = row.PinRaw;
            _isPinRevealed = false;
            UpdatePinRevealState();

            ChkCardActive.IsChecked = row.IsActive;

            BtnBankCardAddOrUpdate.Content = "Update";

            ClearBankCardError();
            ResetBankCardFieldBackgrounds();
        }

        /// <summary>
        /// View button in the ENTRY LINE.
        /// If a row is selected in the grid, performs a "peek" of that card number
        /// into the entry line (masked CVV/PIN, no MessageBox).
        /// If no row is selected, just toggles show/hide for the current entry value.
        /// </summary>
        private void BtnViewCardNumber_Click(object sender, RoutedEventArgs e)
        {
            ClearBankCardError();
            ResetBankCardFieldBackgrounds();

            if (BankCardGrid.SelectedItem is BankCardRow row)
            {
                // We are doing a "peek", not editing.
                _editingRow = null;
                BtnBankCardAddOrUpdate.Content = "Add";

                // Card type for context
                var cardType = _cardTypeItems.FirstOrDefault(ct => ct.ComboDetailId == row.CardTypeId);
                if (cardType != null)
                {
                    CardTypeCombo.SelectedItem = cardType;
                }
                else if (_cardTypeItems.Count > 0)
                {
                    CardTypeCombo.SelectedIndex = 0;
                }
                else
                {
                    CardTypeCombo.SelectedIndex = -1;
                }

                // Load number & expiration
                CardNumberBox.Password = row.CardNumberRaw ?? string.Empty;
                CardNumberPlainTextBox.Text = row.CardNumberRaw ?? string.Empty;

                ExpirationTextBox.Text = row.Expiration;

                // For a view-only peek, do NOT surface CVV or PIN.
                CvvBox.Password = string.Empty;
                CvvPlainTextBox.Text = string.Empty;
                _isCvvRevealed = false;
                UpdateCvvRevealState();

                PinBox.Password = string.Empty;
                PinPlainTextBox.Text = string.Empty;
                _isPinRevealed = false;
                UpdatePinRevealState();

                ChkCardActive.IsChecked = row.IsActive;

                // Reveal card number for easy viewing.
                _isCardNumberRevealed = true;
                UpdateCardNumberRevealState();

                CardNumberPlainTextBox.Focus();
                CardNumberPlainTextBox.SelectAll();
                return;
            }

            // No row selected – just toggle reveal/hide on current entry.
            _isCardNumberRevealed = !_isCardNumberRevealed;
            UpdateCardNumberRevealState();

            if (_isCardNumberRevealed)
            {
                CardNumberPlainTextBox.Focus();
                CardNumberPlainTextBox.SelectAll();
            }
            else
            {
                CardNumberBox.Focus();
                CardNumberBox.SelectAll();
            }
        }

        private void OnBankCardDeleteClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not BankCardRow row)
                return;

            ClearBankCardError();

            if (row.Id != 0)
            {
                ShowBankCardError(
                    "Existing cards can't be deleted here. Edit the card or mark it inactive instead.");
                return;
            }

            if (_editingRow == row)
            {
                ClearEntryFields();
            }

            _bankCardRows.Remove(row);
        }

        // ====================================================================
        // Validation helpers
        // ====================================================================

        private bool ValidateBankCardFields(bool showErrors)
        {
            if (showErrors)
            {
                ResetBankCardFieldBackgrounds();
            }

            var selection = CardTypeCombo.SelectedItem as CardTypeItem;
            string cardNumber = GetCurrentCardNumber();
            string expiration = (ExpirationTextBox.Text ?? "").Trim();

            string cvv = CvvBox.Password ?? string.Empty;
            string pin = PinBox.Password ?? string.Empty;

            if (selection == null)
            {
                if (showErrors)
                    ShowBankCardError("Please choose a card type.", CardTypeCombo);
                return false;
            }

            if (!TryValidateCardNumber(cardNumber, out string cardError))
            {
                if (showErrors)
                    ShowBankCardError(cardError, CardNumberBox);
                return false;
            }

            if (!TryValidateExpiration(expiration, out _, out string expError))
            {
                if (showErrors)
                    ShowBankCardError(expError, ExpirationTextBox);
                return false;
            }

            if (!TryValidateCvv(cvv, out string cvvError))
            {
                if (showErrors)
                    ShowBankCardError(cvvError, CvvBox);
                return false;
            }

            if (!TryValidateCardPin(pin, out string pinError))
            {
                if (showErrors)
                    ShowBankCardError(pinError, PinBox);
                return false;
            }

            if (showErrors)
            {
                ClearBankCardError();
            }

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

            input = input.Trim();
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
            {
                year += 2000;
            }

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
            BankCardErrorTextBlock.Text = message;

            if (field != null)
            {
                field.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x99, 0x99));
            }
        }

        private void ClearBankCardError()
        {
            BankCardErrorTextBlock.Text = string.Empty;
            ResetBankCardFieldBackgrounds();
        }

        private void ResetBankCardFieldBackgrounds()
        {
            CardTypeCombo.ClearValue(BackgroundProperty);
            CardNumberBox.ClearValue(BackgroundProperty);
            CardNumberPlainTextBox.ClearValue(BackgroundProperty);
            ExpirationTextBox.ClearValue(BackgroundProperty);
            CvvBox.ClearValue(BackgroundProperty);
            PinBox.ClearValue(BackgroundProperty);
        }

        private void ClearEntryFields()
        {
            CardTypeCombo.SelectedIndex = _cardTypeItems.Count > 0 ? 0 : -1;

            CardNumberBox.Password = string.Empty;
            CardNumberPlainTextBox.Text = string.Empty;
            _isCardNumberRevealed = false;
            UpdateCardNumberRevealState();

            ExpirationTextBox.Text = string.Empty;

            CvvBox.Password = string.Empty;
            CvvPlainTextBox.Text = string.Empty;
            _isCvvRevealed = false;
            UpdateCvvRevealState();

            PinBox.Password = string.Empty;
            PinPlainTextBox.Text = string.Empty;
            _isPinRevealed = false;
            UpdatePinRevealState();

            ChkCardActive.IsChecked = true;

            _editingRow = null;
            BtnBankCardAddOrUpdate.Content = "Add";

            ClearBankCardError();
            ResetBankCardFieldBackgrounds();
        }

        // ====================================================================
        // DTOs
        // ====================================================================

        public sealed class CardTypeItem
        {
            public int ComboDetailId { get; set; }
            public string Code { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;

            public string DisplayText =>
                string.IsNullOrWhiteSpace(Description) ? Code : Description;
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
                set { if (_cardNumberRaw != value) { _cardNumberRaw = value ?? string.Empty; OnPropertyChanged(nameof(CardNumberRaw)); OnPropertyChanged(nameof(CardNumberMasked)); } }
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
                set { if (_cvvRaw != value) { _cvvRaw = value ?? string.Empty; OnPropertyChanged(nameof(CvvRaw)); OnPropertyChanged(nameof(CvvMasked)); } }
            }

            public string CvvMasked => string.IsNullOrEmpty(_cvvRaw) ? string.Empty : "•••";

            private string _pinRaw = string.Empty;
            public string PinRaw
            {
                get => _pinRaw;
                set { if (_pinRaw != value) { _pinRaw = value ?? string.Empty; OnPropertyChanged(nameof(PinRaw)); OnPropertyChanged(nameof(PinMasked)); } }
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
