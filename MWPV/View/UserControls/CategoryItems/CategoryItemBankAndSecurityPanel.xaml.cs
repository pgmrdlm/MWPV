using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MWPV.Models;
using MWPV.Services;

namespace MWPV.View.UserControls.CategoryItems
{
    public partial class CategoryItemBankAndSecurityPanel : UserControl
    {
        // In-memory collection of card rows for this item.
        private readonly ObservableCollection<BankCardRow> _bankCardRows = new();
        private readonly ObservableCollection<CardTypeItem> _cardTypeItems = new();

        // Currently edited row (null means "Add" mode).
        private BankCardRow? _editingRow;

        // Mode flag: parent should set this to false when editing an existing Category Item.
        public bool IsNewCategoryItem { get; set; } = true;

        // Default background cache for restoring after validation.
        private readonly Brush _defaultInputBackground = SystemColors.WindowBrush;

        public ObservableCollection<BankCardRow> BankCardRows => _bankCardRows;
        public ObservableCollection<CardTypeItem> CardTypeItems => _cardTypeItems;

        public CategoryItemBankAndSecurityPanel()
        {
            InitializeComponent();

            DataContext = this;

            Debug.WriteLine("[BANK-PANEL] Loaded");
            LoadBankCardTypes();
        }

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

        // ======== Button handlers ========

        private void OnBankCardAddOrUpdateClick(object sender, RoutedEventArgs e)
        {
            ClearValidationHighlights();

            var selection = CardTypeCombo.SelectedItem as CardTypeItem;
            string cardNumberRaw = (CardNumberTextBox.Text ?? string.Empty).Trim();
            string expirationRaw = (ExpirationTextBox.Text ?? string.Empty).Trim();
            string cvvRaw = CvvBox.Password ?? string.Empty;
            string pinRaw = PinBox.Password ?? string.Empty;
            bool isActive = ChkCardActive.IsChecked == true;

            bool hasError = false;
            Control? firstInvalid = null;
            string errorMessage = string.Empty;

            // Helper to mark a control invalid and capture first one.
            void MarkInvalid(Control control, string message)
            {
                if (!hasError)
                {
                    hasError = true;
                    errorMessage = message;
                }

                control.Background = Brushes.LightCoral;

                if (firstInvalid == null)
                    firstInvalid = control;
            }

            // 1) Card type required
            if (selection == null)
            {
                MarkInvalid(CardTypeCombo, "Please choose a card type.");
            }

            // 2) Card number: digits + spaces only, 12–19 digits, required
            string normalizedCardDigits = NormalizeDigits(cardNumberRaw);
            bool cardHasInvalidChars = cardNumberRaw.Any(c => !char.IsDigit(c) && !char.IsWhiteSpace(c));

            if (string.IsNullOrWhiteSpace(cardNumberRaw))
            {
                MarkInvalid(CardNumberTextBox, "Card number is required.");
            }
            else if (cardHasInvalidChars)
            {
                MarkInvalid(CardNumberTextBox, "Card number must contain digits and spaces only.");
            }
            else if (normalizedCardDigits.Length < 12 || normalizedCardDigits.Length > 19)
            {
                MarkInvalid(CardNumberTextBox, "Card number must be 12–19 digits.");
            }

            // 3) Expiration: required, MM/YY or MM/YYYY, not in the past
            string expirationCanonical = string.Empty;
            if (string.IsNullOrWhiteSpace(expirationRaw))
            {
                MarkInvalid(ExpirationTextBox, "Expiration date is required.");
            }
            else
            {
                if (!TryParseExpiration(expirationRaw, out int expYear, out int expMonth, out expirationCanonical, out string expError))
                {
                    MarkInvalid(ExpirationTextBox, expError);
                }
                else
                {
                    var now = DateTime.UtcNow;
                    var currentMonth = new DateTime(now.Year, now.Month, 1);
                    var expMonthDate = new DateTime(expYear, expMonth, 1);

                    if (expMonthDate < currentMonth)
                    {
                        MarkInvalid(ExpirationTextBox, "Expiration date must be this month or later.");
                    }
                }
            }

            // 4) CVV: optional, if provided 3–4 digits
            if (!string.IsNullOrWhiteSpace(cvvRaw))
            {
                if (!cvvRaw.All(char.IsDigit))
                {
                    MarkInvalid(CvvBox, "CVV must be numeric.");
                }
                else if (cvvRaw.Length < 3 || cvvRaw.Length > 4)
                {
                    MarkInvalid(CvvBox, "CVV must be 3–4 digits.");
                }
            }

            // 5) PIN: optional, if provided 4–8 digits
            if (!string.IsNullOrWhiteSpace(pinRaw))
            {
                if (!pinRaw.All(char.IsDigit))
                {
                    MarkInvalid(PinBox, "PIN must be numeric.");
                }
                else if (pinRaw.Length < 4 || pinRaw.Length > 8)
                {
                    MarkInvalid(PinBox, "PIN must be 4–8 digits.");
                }
            }

            // If any basic field errors, stop here.
            if (hasError)
            {
                BankCardErrorTextBlock.Text = errorMessage;
                firstInvalid?.Focus();
                return;
            }

            // At this point, selection and parsing are safe to use.
            int cardTypeId = selection!.ComboDetailId;

            // 6) Duplicate rules
            var candidates = _bankCardRows.Where(r => !ReferenceEquals(r, _editingRow)).ToList();

            // 6a) Exact duplicate of (CardType, CardNumber, Expiration)
            bool duplicateExact = candidates.Any(r =>
                r.CardTypeId == cardTypeId &&
                string.Equals(NormalizeDigits(r.CardNumberRaw), normalizedCardDigits, StringComparison.Ordinal) &&
                string.Equals(CanonicalizeExpiration(r.Expiration), expirationCanonical, StringComparison.Ordinal));

            if (duplicateExact)
            {
                MarkInvalid(CardTypeCombo, "This card is already listed for this item.");
                MarkInvalid(CardNumberTextBox, "This card is already listed for this item.");
                MarkInvalid(ExpirationTextBox, "This card is already listed for this item.");
            }

            // 6b) CardType duplication rules
            if (IsNewCategoryItem)
            {
                // New Category Item: no duplicate CardType at all.
                bool duplicateType = candidates.Any(r => r.CardTypeId == cardTypeId);
                if (duplicateType)
                {
                    MarkInvalid(CardTypeCombo, "Only one card of each type is allowed when creating a new item.");
                }
            }
            else
            {
                // Editing existing: at most one active row per CardType.
                if (isActive)
                {
                    bool otherActive = candidates.Any(r => r.CardTypeId == cardTypeId && r.IsActive);
                    if (otherActive)
                    {
                        MarkInvalid(CardTypeCombo, "Only one active card of this type is allowed; mark the old card inactive first.");
                    }
                }
            }

            if (hasError)
            {
                BankCardErrorTextBlock.Text = errorMessage;
                firstInvalid?.Focus();
                return;
            }

            // ======== No validation errors: proceed with Add/Update ========

            if (_editingRow == null)
            {
                // Add mode
                var row = new BankCardRow
                {
                    Id = 0,
                    CardTypeId = cardTypeId,
                    CardTypeDisplay = selection.DisplayText,
                    CardNumberRaw = cardNumberRaw,
                    Expiration = expirationRaw,
                    CvvRaw = cvvRaw,
                    PinRaw = pinRaw,
                    IsActive = isActive
                };

                _bankCardRows.Add(row);
            }
            else
            {
                // Update existing row
                _editingRow.CardTypeId = cardTypeId;
                _editingRow.CardTypeDisplay = selection.DisplayText;
                _editingRow.CardNumberRaw = cardNumberRaw;
                _editingRow.Expiration = expirationRaw;
                _editingRow.CvvRaw = cvvRaw;
                _editingRow.PinRaw = pinRaw;
                _editingRow.IsActive = isActive;
            }

            BankCardErrorTextBlock.Text = string.Empty;
            ClearEntryFields();
        }

        private void OnBankCardClearClick(object sender, RoutedEventArgs e)
        {
            ClearEntryFields();
            ClearValidationHighlights();
            BankCardErrorTextBlock.Text = string.Empty;
        }

        private void ClearEntryFields()
        {
            CardTypeCombo.SelectedIndex = _cardTypeItems.Count > 0 ? 0 : -1;
            CardNumberTextBox.Text = string.Empty;
            ExpirationTextBox.Text = string.Empty;
            CvvBox.Password = string.Empty;
            PinBox.Password = string.Empty;
            ChkCardActive.IsChecked = true;

            _editingRow = null;
            if (BtnBankCardAddOrUpdate != null)
            {
                BtnBankCardAddOrUpdate.Content = "Add";
            }
        }

        private void OnBankCardEditClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not BankCardRow row)
                return;

            ClearValidationHighlights();
            BankCardErrorTextBlock.Text = string.Empty;

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
            PinBox.Password = row.PinRaw;
            ChkCardActive.IsChecked = row.IsActive;

            BtnBankCardAddOrUpdate.Content = "Update";
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
                ClearValidationHighlights();
                BankCardErrorTextBlock.Text = string.Empty;
            }

            _bankCardRows.Remove(row);
        }

        // ======== Validation helpers ========

        private void ClearValidationHighlights()
        {
            CardTypeCombo.Background = _defaultInputBackground;
            CardNumberTextBox.Background = _defaultInputBackground;
            ExpirationTextBox.Background = _defaultInputBackground;
            CvvBox.Background = _defaultInputBackground;
            PinBox.Background = _defaultInputBackground;
        }

        private static string NormalizeDigits(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return new string(value.Where(char.IsDigit).ToArray());
        }

        private static bool TryParseExpiration(
            string input,
            out int year,
            out int month,
            out string canonical,
            out string errorMessage)
        {
            year = 0;
            month = 0;
            canonical = string.Empty;
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(input))
            {
                errorMessage = "Expiration date is required.";
                return false;
            }

            string trimmed = input.Trim();
            string[] parts = trimmed.Split('/');

            if (parts.Length != 2)
            {
                errorMessage = "Expiration must be in MM/YY or MM/YYYY format.";
                return false;
            }

            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out month) ||
                month < 1 || month > 12)
            {
                errorMessage = "Month must be between 01 and 12.";
                return false;
            }

            if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out year))
            {
                errorMessage = "Year is invalid.";
                return false;
            }

            // Convert 2-digit year to 2000–2099 range.
            if (year < 100)
            {
                year += 2000;
            }

            if (year < 2000 || year > 2099)
            {
                errorMessage = "Year is out of valid range.";
                return false;
            }

            canonical = $"{year:D4}-{month:D2}";
            return true;
        }

        private static string CanonicalizeExpiration(string input)
        {
            if (TryParseExpiration(input, out int y, out int m, out var canonical, out _))
                return canonical;

            return string.Empty;
        }

        // ======== Helper DTOs ========

        public sealed class CardTypeItem
        {
            public int ComboDetailId { get; set; }
            public string Code { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;

            public string DisplayText =>
                string.IsNullOrWhiteSpace(Description) ? Code : Description;
        }

        /// <summary>
        /// Lightweight in-memory representation of a bank card row for the grid.
        /// We keep raw values so we can later convert these into the encrypted
        /// BankCard model when the parent window saves.
        /// </summary>
        public sealed class BankCardRow : INotifyPropertyChanged
        {
            public int Id { get; set; }  // 0 = new, not yet persisted

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
                set
                {
                    if (_expiration != value)
                    {
                        _expiration = value ?? string.Empty;
                        OnPropertyChanged(nameof(Expiration));
                    }
                }
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
                set
                {
                    if (_isActive != value)
                    {
                        _isActive = value;
                        OnPropertyChanged(nameof(IsActive));
                    }
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged(string propertyName) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
