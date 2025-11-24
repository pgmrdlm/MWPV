using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
            var selection = CardTypeCombo.SelectedItem as CardTypeItem;
            if (selection == null)
            {
                MessageBox.Show("Please choose a card type.", "Bank Cards",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string cardNumber = (CardNumberTextBox.Text ?? "").Trim();
            string expiration = (ExpirationTextBox.Text ?? "").Trim();
            string cvv = CvvBox.Password ?? string.Empty;
            string pin = PinBox.Password ?? string.Empty;
            bool isActive = ChkCardActive.IsChecked == true;

            if (string.IsNullOrWhiteSpace(cardNumber))
            {
                MessageBox.Show("Card number is required.", "Bank Cards",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(expiration))
            {
                MessageBox.Show("Expiration date is required.", "Bank Cards",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_editingRow == null)
            {
                // Add mode
                var row = new BankCardRow
                {
                    Id = 0,
                    CardTypeId = selection.ComboDetailId,
                    CardTypeDisplay = selection.DisplayText,
                    CardNumberRaw = cardNumber,
                    Expiration = expiration,
                    CvvRaw = cvv,
                    PinRaw = pin,
                    IsActive = isActive
                };

                _bankCardRows.Add(row);
            }
            else
            {
                // Update existing row
                _editingRow.CardTypeId = selection.ComboDetailId;
                _editingRow.CardTypeDisplay = selection.DisplayText;
                _editingRow.CardNumberRaw = cardNumber;
                _editingRow.Expiration = expiration;
                _editingRow.CvvRaw = cvv;
                _editingRow.PinRaw = pin;
                _editingRow.IsActive = isActive;
            }

            ClearEntryFields();
        }

        private void OnBankCardClearClick(object sender, RoutedEventArgs e)
        {
            ClearEntryFields();
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
            }

            _bankCardRows.Remove(row);
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
