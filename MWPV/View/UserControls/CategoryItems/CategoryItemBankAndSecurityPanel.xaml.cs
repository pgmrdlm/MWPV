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
    public partial class CategoryItemBankAndSecurityPanel : UserControl
    {
        // Cards
        private readonly ObservableCollection<BankCardRow> _bankCardRows = new();
        private readonly ObservableCollection<CardTypeItem> _cardTypeItems = new();
        private BankCardRow? _editingRow;

        // Reveal state for CVV/PIN on the entry line
        private bool _isCvvRevealed;
        private bool _isPinRevealed;

        // Accounts
        private readonly ObservableCollection<AccountRow> _accountRows = new();
        private readonly ObservableCollection<AccountTypeItem> _accountTypeItems = new();
        private AccountRow? _editingAccountRow;

        public ObservableCollection<BankCardRow> BankCardRows => _bankCardRows;
        public ObservableCollection<CardTypeItem> CardTypeItems => _cardTypeItems;

        public ObservableCollection<AccountRow> AccountRows => _accountRows;
        public ObservableCollection<AccountTypeItem> AccountTypeItems => _accountTypeItems;

        public CategoryItemBankAndSecurityPanel()
        {
            InitializeComponent();

            DataContext = this;

            Debug.WriteLine("[BANK-PANEL] Loaded");
            LoadBankCardTypes();
            LoadAccountTypes();

            // Ensure reveal state starts in masked mode
            _isCvvRevealed = false;
            _isPinRevealed = false;
            UpdateCvvRevealState();
            UpdatePinRevealState();
        }

        // ====================================================================
        // CVV / PIN reveal handlers (entry line only)
        // ====================================================================

        private void CvvBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            // Keep the plain-text overlay in sync so we can flip instantly.
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

        private void UpdateCvvRevealState()
        {
            if (CvvBox == null || CvvPlainTextBox == null || BtnToggleCvvReveal == null)
                return;

            if (_isCvvRevealed)
            {
                // Show plain text, hide PasswordBox
                CvvPlainTextBox.Text = CvvBox.Password;
                CvvPlainTextBox.Visibility = Visibility.Visible;
                CvvPlainTextBox.IsHitTestVisible = true;   // allow select/copy

                CvvBox.Visibility = Visibility.Collapsed;
                CvvBox.IsEnabled = false;

                BtnToggleCvvReveal.ToolTip = "Hide CVV";
            }
            else
            {
                // Go back to masked input
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

        // ====================================================================
        // Load combos
        // ====================================================================

        private void LoadBankCardTypes()
        {
            try
            {
                const int comboTypeId = 2; // bank card types
                Debug.WriteLine($"[BANK-PANEL][BANK-CARD] Loading card types for ComboTypeId='{comboTypeId}'");

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

                Debug.WriteLine($"[BANK-PANEL][BANK-CARD] Loaded {_cardTypeItems.Count} card types.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BANK-PANEL][BANK-CARD][ERROR] {ex}");
                MessageBox.Show(
                    "Unable to load bank card types. Bank card entry will be disabled for this session.",
                    "Bank Cards",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void LoadAccountTypes()
        {
            try
            {
                const int comboTypeId = 1; // account_types
                Debug.WriteLine($"[BANK-PANEL][ACCOUNTS] Loading account types for ComboTypeId='{comboTypeId}'");

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
                        Description = string.IsNullOrWhiteSpace(t.Description)
                            ? t.Code
                            : t.Description
                    });
                }

                Debug.WriteLine($"[BANK-PANEL][ACCOUNTS] Loaded {_accountTypeItems.Count} account types.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BANK-PANEL][ACCOUNTS][ERROR] {ex}");
                MessageBox.Show(
                    "Unable to load account types. Account entry will be disabled for this session.",
                    "Accounts",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // ====================================================================
        // CARDS - Button handlers
        // ====================================================================

        private void OnBankCardAddOrUpdateClick(object sender, RoutedEventArgs e)
        {
            ClearBankCardError();

            // Full validation pass
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

            string cardNumber = (CardNumberTextBox.Text ?? "").Trim();
            string expirationInput = (ExpirationTextBox.Text ?? "").Trim();

            // Use whatever is currently the source of truth for CVV/PIN
            string cvv = _isCvvRevealed ? (CvvPlainTextBox.Text ?? string.Empty) : (CvvBox.Password ?? string.Empty);
            string pin = _isPinRevealed ? (PinPlainTextBox.Text ?? string.Empty) : (PinBox.Password ?? string.Empty);

            bool isActive = ChkCardActive.IsChecked == true;

            // We already validated, so this should succeed. We re-run to get the normalized value.
            if (!TryValidateExpiration(expirationInput, out string expNormalized, out _))
            {
                ShowBankCardError("Expiration date is invalid.", ExpirationTextBox);
                return;
            }

            if (_editingRow == null)
            {
                // When creating a new item: only one card per type
                bool duplicateType = _bankCardRows.Any(r => r.CardTypeId == selection.ComboDetailId);
                if (duplicateType)
                {
                    ShowBankCardError("Only one card of each type is allowed when creating a new item.", CardTypeCombo);
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
            // Re-run validation on field exit so errors move/clear as the user fixes things.
            ValidateBankCardFields(showErrors: true);
        }

        private void ClearEntryFields()
        {
            CardTypeCombo.SelectedIndex = _cardTypeItems.Count > 0 ? 0 : -1;
            CardNumberTextBox.Text = string.Empty;
            ExpirationTextBox.Text = string.Empty;

            // Clear both masked and plain fields and reset reveal state
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
            if (BtnBankCardAddOrUpdate != null)
            {
                BtnBankCardAddOrUpdate.Content = "Add";
            }

            ClearBankCardError();
            ResetBankCardFieldBackgrounds();
        }

        private void OnBankCardEditClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not BankCardRow row)
                return;

            _editingRow = row;

            // Push values back into the entry line
            var cardType = _cardTypeItems.FirstOrDefault(ct => ct.ComboDetailId == row.CardTypeId);
            if (cardType != null)
            {
                CardTypeCombo.SelectedItem = cardType;
            }

            CardNumberTextBox.Text = row.CardNumberRaw;
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

        private void OnBankCardDeleteClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not BankCardRow row)
                return;

            if (MessageBox.Show("Remove this card from the list?",
                                "Bank Cards",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Question,
                                MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                return;
            }

            if (_editingRow == row)
            {
                ClearEntryFields();
            }

            _bankCardRows.Remove(row);
        }

        // ====================================================================
        // CARDS - Validation helpers
        // ====================================================================

        private bool ValidateBankCardFields(bool showErrors)
        {
            if (showErrors)
            {
                ResetBankCardFieldBackgrounds();
            }

            var selection = CardTypeCombo.SelectedItem as CardTypeItem;
            string cardNumber = (CardNumberTextBox.Text ?? "").Trim();
            string expiration = (ExpirationTextBox.Text ?? "").Trim();

            // Work from the masked fields as the canonical inputs
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
                    ShowBankCardError(cardError, CardNumberTextBox);
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
                // optional
                errorMessage = string.Empty;
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
                // optional
                errorMessage = string.Empty;
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

            // 2-digit → 2000-based 4-digit
            if (year < 100)
            {
                year += 2000;
            }

            // Cap at 5 years from the current year
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
                field.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x99, 0x99)); // light red
            }
        }

        private void ClearBankCardError()
        {
            BankCardErrorTextBlock.Text = string.Empty;
            ResetBankCardFieldBackgrounds();
        }

        private void ResetBankCardFieldBackgrounds()
        {
            CardNumberTextBox.ClearValue(BackgroundProperty);
            ExpirationTextBox.ClearValue(BackgroundProperty);
            CvvBox.ClearValue(BackgroundProperty);
            PinBox.ClearValue(BackgroundProperty);
            CardTypeCombo.ClearValue(BackgroundProperty);
        }

        // ====================================================================
        // ACCOUNTS - handlers & helpers (unchanged)
        // ====================================================================

        private void OnAccountAddOrUpdateClick(object sender, RoutedEventArgs e)
        {
            ClearAccountError();

            var selection = AccountNameCombo.SelectedItem as AccountTypeItem;
            if (selection == null)
            {
                ShowAccountError("Please choose an account name.", AccountNameCombo);
                return;
            }

            string accountNumber = (AccountNumberTextBox.Text ?? "").Trim();
            string pin = AccountPinBox.Password ?? string.Empty;
            bool isActive = ChkAccountActive.IsChecked == true;

            if (string.IsNullOrWhiteSpace(accountNumber))
            {
                ShowAccountError("Account number is required.", AccountNumberTextBox);
                return;
            }

            var validChars = new string(accountNumber.Where(c => char.IsDigit(c) || c == ' ').ToArray());
            if (!string.Equals(validChars, accountNumber, StringComparison.Ordinal))
            {
                ShowAccountError("Account number must contain digits and spaces only.", AccountNumberTextBox);
                return;
            }

            if (!string.IsNullOrWhiteSpace(pin))
            {
                if (!pin.All(char.IsDigit) || pin.Length < 4 || pin.Length > 12)
                {
                    ShowAccountError("Account PIN must be 4–12 digits.", AccountPinBox);
                    return;
                }
            }

            if (_editingAccountRow == null)
            {
                var row = new AccountRow
                {
                    Id = 0,
                    AccountTypeId = selection.ComboDetailId,
                    AccountTypeDisplay = selection.DisplayText,
                    AccountNumberRaw = accountNumber,
                    PinRaw = pin,
                    IsActive = isActive
                };

                _accountRows.Add(row);
            }
            else
            {
                _editingAccountRow.AccountTypeId = selection.ComboDetailId;
                _editingAccountRow.AccountTypeDisplay = selection.DisplayText;
                _editingAccountRow.AccountNumberRaw = accountNumber;
                _editingAccountRow.PinRaw = pin;
                _editingAccountRow.IsActive = isActive;
            }

            ClearAccountEntryFields();
        }

        private void OnAccountClearClick(object sender, RoutedEventArgs e)
        {
            ClearAccountError();
            ClearAccountEntryFields();
        }

        private void OnAccountEditClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not AccountRow row)
                return;

            _editingAccountRow = row;

            var acctType = _accountTypeItems.FirstOrDefault(a => a.ComboDetailId == row.AccountTypeId);
            if (acctType != null)
            {
                AccountNameCombo.SelectedItem = acctType;
            }

            AccountNumberTextBox.Text = row.AccountNumberRaw;
            AccountPinBox.Password = row.PinRaw;
            ChkAccountActive.IsChecked = row.IsActive;

            BtnAccountAddOrUpdate.Content = "Update";

            ClearAccountError();
            ResetAccountFieldBackgrounds();
        }

        private void OnAccountDeleteClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not AccountRow row)
                return;

            if (MessageBox.Show("Remove this account from the list?",
                                "Accounts",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Question,
                                MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                return;
            }

            if (_editingAccountRow == row)
            {
                ClearAccountEntryFields();
            }

            _accountRows.Remove(row);
        }

        private void ClearAccountEntryFields()
        {
            AccountNameCombo.SelectedIndex = _accountTypeItems.Count > 0 ? 0 : -1;
            AccountNumberTextBox.Text = string.Empty;
            AccountPinBox.Password = string.Empty;
            ChkAccountActive.IsChecked = true;

            _editingAccountRow = null;
            BtnAccountAddOrUpdate.Content = "Add";

            ClearAccountError();
            ResetAccountFieldBackgrounds();
        }

        private void ShowAccountError(string message, Control? field = null)
        {
            AccountErrorTextBlock.Text = message;

            if (field != null)
            {
                field.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x99, 0x99));
            }
        }

        private void ClearAccountError()
        {
            AccountErrorTextBlock.Text = string.Empty;
            ResetAccountFieldBackgrounds();
        }

        private void ResetAccountFieldBackgrounds()
        {
            AccountNameCombo.ClearValue(BackgroundProperty);
            AccountNumberTextBox.ClearValue(BackgroundProperty);
            AccountPinBox.ClearValue(BackgroundProperty);
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

        public sealed class AccountTypeItem
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

        public sealed class AccountRow : INotifyPropertyChanged
        {
            public int Id { get; set; }

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

            private string _accountNumberRaw = string.Empty;
            public string AccountNumberRaw
            {
                get => _accountNumberRaw;
                set { if (_accountNumberRaw != value) { _accountNumberRaw = value ?? string.Empty; OnPropertyChanged(nameof(AccountNumberRaw)); OnPropertyChanged(nameof(AccountNumberMasked)); } }
            }

            public string AccountNumberMasked
            {
                get
                {
                    if (string.IsNullOrWhiteSpace(_accountNumberRaw))
                        return string.Empty;

                    var digits = new string(_accountNumberRaw.Where(char.IsDigit).ToArray());
                    if (digits.Length <= 4)
                        return "•••• " + digits;

                    return "•••• " + digits[^4..];
                }
            }

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
