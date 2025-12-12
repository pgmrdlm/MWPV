using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Security.Utility.Validation;
using MWPV.Utilities.Helpers;
using MWPV.Utilities.UI; // UICleaner

namespace MWPV.View.UserControls
{
    public partial class CategoryItemEditorTabs : UserControl
    {
        public event EventHandler? Submitted;
        public event EventHandler? Canceled;
        public event EventHandler<string>? PasswordValidationFailed;

        private int _categoryKey;
        private string _categoryName = string.Empty;
        private bool _isEditMode;

        // Reveal state flags
        private bool _mainRevealed;
        private bool _verifyRevealed;
        private bool _phoneRevealed;

        // Centralized helper replaces DispatcherTimer usage
        private readonly AutoHideTimer _revealAutoHide;

        private bool _settingPwProgrammatically;

        // Email visuals
        private Brush? _emailDefaultBorderBrush;
        private Brush? _emailDefaultBackground;
        private string _lastEmailChecked = string.Empty;

        // Phone visuals
        private Brush? _phoneDefaultBorderBrush;
        private Brush? _phoneDefaultBackground;

        // Item name visuals
        private Brush? _itemNameDefaultBorderBrush;
        private Brush? _itemNameDefaultBackground;

        // Prevent event stacking if control is reloaded into the visual tree
        private bool _uiEventsHooked;

        public CategoryItemEditorTabs()
        {
            InitializeComponent();

            Loaded += CategoryItemEditorTabs_Loaded;
            Unloaded += CategoryItemEditorTabs_Unloaded;

            _revealAutoHide = new AutoHideTimer(
                interval: TimeSpan.FromSeconds(20),
                onTimeout: OnRevealTimeout
            );
        }

        private void CategoryItemEditorTabs_Loaded(object? sender, RoutedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[ITEM-TABS] Loaded");
#endif
            CacheDefaultFieldVisualsIfNeeded();
            HookUiEventsOnce();

            ClearForm();
            ResetUiState();
        }

        private void CategoryItemEditorTabs_Unloaded(object? sender, RoutedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[ITEM-TABS] Unloaded");
#endif
            _revealAutoHide.Stop();

            UnhookUiEvents();

            ResetUiState();
            WipeSensitiveFields();
        }

        public void ConfigureForAdd(int categoryKey, string categoryName)
        {
            _categoryKey = categoryKey;
            _categoryName = categoryName;
            _isEditMode = false;

#if DEBUG
            Debug.WriteLine($"[ITEM-TABS] ConfigureForAdd: key={categoryKey}, name='{categoryName}'");
#endif
            ClearForm();
            ResetUiState();
        }

        public void ConfigureForEdit(int categoryKey, string categoryName, object existingItem)
        {
            _categoryKey = categoryKey;
            _categoryName = categoryName;
            _isEditMode = true;

#if DEBUG
            Debug.WriteLine($"[ITEM-TABS] ConfigureForEdit: key={categoryKey}, name='{categoryName}'");
#endif
            // TODO: Map existingItem -> fields once persistence is wired
            ResetUiState();
        }

        /* ======================= Submit / Cancel ======================= */

        private void btnSubmit_Click(object? sender, RoutedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[ITEM-TABS] Submit clicked");
#endif
            // SAVE RULE: show ALL blocking errors (required + invalid formats) in one pass.
            // Focus goes to the FIRST field with an error in our agreed order.

            bool isBookmarkOnly = chkBookmarkOnly.IsChecked == true;

            bool okName = ValidateItemName(forSubmit: true);

            // Password rules:
            // - Required unless Bookmark-only
            // - If bookmark-only: we skip all password validation and hide password-related panels
            bool okPassword = ValidatePasswordForSubmission(isBookmarkOnly, out _);

            // Email / Phone are only errors if user entered a value
            bool okEmail = ValidateEmailForSubmit();
            bool okPhone = ValidatePhoneNumber(forSubmit: true);

            bool anyError = !(okName && okPassword && okEmail && okPhone);
            if (anyError)
            {
                FocusFirstError(okName, okPassword, okEmail, okPhone, isBookmarkOnly);
                return;
            }

            try
            {
                Submitted?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                // TODO: Replace with app-standard dialog / ErrorHandler when we wire it in.
                MessageBox.Show("Error saving item.\n\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void FocusFirstError(bool okName, bool okPassword, bool okEmail, bool okPhone, bool isBookmarkOnly)
        {
            if (!okName)
            {
                txtItemName.Focus();
                return;
            }

            if (!okPassword)
            {
                // If verify is visible and non-empty mismatch exists, it’s nicer to land there.
                if (!isBookmarkOnly && VerifyRow.Visibility == Visibility.Visible)
                {
                    var verify = pwdVerify.Password ?? string.Empty;
                    if (!string.IsNullOrEmpty(verify))
                        pwdVerify.Focus();
                    else
                        pwdPassword.Focus();
                }
                else
                {
                    pwdPassword.Focus();
                }
                return;
            }

            if (!okEmail)
            {
                txtEmail.Focus();
                return;
            }

            if (!okPhone)
            {
                txtPhone.Focus();
                return;
            }
        }

        private void btnCancel_Click(object? sender, RoutedEventArgs e)
        {
            ResetUiState();
            WipeSensitiveFields();

            Canceled?.Invoke(this, EventArgs.Empty);
        }

        /* ======================= Shared reveal auto-hide ======================= */

        private void OnRevealTimeout()
        {
#if DEBUG
            Debug.WriteLine("[ITEM-TABS] Reveal timer elapsed – hiding all reveals");
#endif
            HideAllRevealsAndStopTimer();
            ClearPlainRevealOverlays();
        }

        private void HideAllRevealsAndStopTimer()
        {
            _revealAutoHide.Stop();

            HideMainPassword();
            HideVerifyPassword();
            HidePhone();
        }

        private void TouchRevealTimerIfNeeded()
        {
            bool anyRevealed = _mainRevealed || _verifyRevealed || _phoneRevealed;
            _revealAutoHide.Touch(anyRevealed);
        }

        /* ======================= Item name validation (new error row) ======================= */
        // NOTE: XAML must contain:
        //   StackPanel x:Name="ItemNameErrorPanel" Visibility="Collapsed"
        //   TextBlock  x:Name="ItemNameErrorText"
        // matching the pattern we use for EmailErrorPanel/PhoneErrorPanel.

        private bool ValidateItemName(bool forSubmit)
        {
            var name = (txtItemName.Text ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                ShowItemNameError("Item name is required.");
                return false;
            }

            ClearItemNameError();
            return true;
        }

        private void ShowItemNameError(string message)
        {
            if (ItemNameErrorText != null)
                ItemNameErrorText.Text = message ?? string.Empty;

            if (ItemNameErrorPanel != null && ItemNameErrorPanel.Visibility != Visibility.Visible)
                ItemNameErrorPanel.Visibility = Visibility.Visible;

            txtItemName.ToolTip = message;

            var fill = TryFindResource("FieldErrorFill") as Brush
                       ?? new SolidColorBrush(Color.FromRgb(0xFF, 0xEE, 0xEE));
            txtItemName.Background = fill;
        }

        private void ClearItemNameError()
        {
            if (ItemNameErrorText != null)
                ItemNameErrorText.Text = string.Empty;

            if (ItemNameErrorPanel != null && ItemNameErrorPanel.Visibility != Visibility.Collapsed)
                ItemNameErrorPanel.Visibility = Visibility.Collapsed;

            txtItemName.ToolTip = null;
            if (_itemNameDefaultBackground != null)
                txtItemName.Background = _itemNameDefaultBackground;
            if (_itemNameDefaultBorderBrush != null)
                txtItemName.BorderBrush = _itemNameDefaultBorderBrush;
        }

        /* ======================= Password / Verify ======================= */

        private void BtnGeneratePassword_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var generated = SecurePassword.GenerateAsString(12);

                _settingPwProgrammatically = true;
                try
                {
                    SetPassword(generated);
                }
                finally
                {
                    _settingPwProgrammatically = false;
                }

                HideStrengthRow();
                HideVerifyRow();
                HideVerifyError();
            }
            catch
            {
                PasswordValidationFailed?.Invoke(this, "Password generator failed.");
            }
        }

        private void BtnTogglePasswordReveal_Click(object? sender, RoutedEventArgs e)
        {
            if (_mainRevealed)
                HideMainPassword();
            else
                ShowMainPassword();

            TouchRevealTimerIfNeeded();
        }

        private void BtnToggleVerifyReveal_Click(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(pwdVerify.Password))
                return;

            if (_verifyRevealed)
                HideVerifyPassword();
            else
                ShowVerifyPassword();

            TouchRevealTimerIfNeeded();
        }

        private void PwdPassword_PreviewKeyDown(object? sender, KeyEventArgs e)
        {
            if (_settingPwProgrammatically) return;

            bool pasteCombo =
                (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) ||
                (e.Key == Key.Insert && (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift);

            if (pasteCombo)
            {
                HideVerifyRow();
                HideVerifyError();
            }
        }

        private void pwdPassword_PasswordChanged(object? sender, RoutedEventArgs e)
        {
            if (_mainRevealed)
                txtPasswordPlain.Text = pwdPassword.Password;

            TouchRevealTimerIfNeeded();

            // If bookmark-only, we treat password as "ignored" and keep the UI clean.
            if (chkBookmarkOnly.IsChecked == true)
            {
                HideStrengthRow();
                HideVerifyRow();
                HideVerifyError();
                return;
            }

            if (_settingPwProgrammatically) return;

            if (!string.IsNullOrEmpty(pwdPassword.Password))
                EnsureVerifyRowVisibleForManual();
            else
            {
                HideVerifyRow();
                HideVerifyError();
            }

            EvaluateAndDisplayVerifyMismatch();
            UpdateStrengthPanelForPolicy();
        }

        private void pwdPassword_GotFocus(object? sender, RoutedEventArgs e)
        {
            if (_settingPwProgrammatically) return;
            if (chkBookmarkOnly.IsChecked == true) return;

            if (!string.IsNullOrEmpty(pwdPassword.Password))
                UpdateStrengthPanelForPolicy();
        }

        private void pwdVerify_PasswordChanged(object? sender, RoutedEventArgs e)
        {
            if (_verifyRevealed)
                txtVerifyPlain.Text = pwdVerify.Password;

            TouchRevealTimerIfNeeded();

            if (string.IsNullOrEmpty(pwdVerify.Password))
            {
                btnToggleVerifyReveal.Visibility = Visibility.Collapsed;
                HideVerifyPassword();
            }
            else
            {
                btnToggleVerifyReveal.Visibility = Visibility.Visible;
            }

            EvaluateAndDisplayVerifyMismatch();
        }

        private void ShowMainPassword()
        {
            txtPasswordPlain.Text = pwdPassword.Password;
            txtPasswordPlain.Visibility = Visibility.Visible;
            pwdPassword.Visibility = Visibility.Collapsed;
            _mainRevealed = true;
        }

        private void HideMainPassword()
        {
            UICleaner.Clear(txtPasswordPlain);
            txtPasswordPlain.Visibility = Visibility.Collapsed;
            pwdPassword.Visibility = Visibility.Visible;
            _mainRevealed = false;
        }

        private void ShowVerifyPassword()
        {
            txtVerifyPlain.Text = pwdVerify.Password;
            txtVerifyPlain.Visibility = Visibility.Visible;
            pwdVerify.Visibility = Visibility.Collapsed;
            _verifyRevealed = true;
        }

        private void HideVerifyPassword()
        {
            UICleaner.Clear(txtVerifyPlain);
            txtVerifyPlain.Visibility = Visibility.Collapsed;
            pwdVerify.Visibility = Visibility.Visible;
            _verifyRevealed = false;
        }

        private void SetPassword(string value)
        {
            pwdPassword.Password = value;

            if (_mainRevealed)
                txtPasswordPlain.Text = value;

            HideVerifyRow();
            HideVerifyError();
        }

        private bool ValidatePasswordForSubmission(bool isBookmarkOnly, out string error)
        {
            error = string.Empty;

            if (isBookmarkOnly)
            {
                HideStrengthRow();
                HideVerifyRow();
                HideVerifyError();
                return true;
            }

            var pw = pwdPassword.Password ?? string.Empty;

            // Required on Save unless bookmark-only
            if (string.IsNullOrEmpty(pw))
            {
                HideStrengthRow();
                ShowVerifyError("Password is required.");
                return false;
            }

            // Verify mismatch (only enforced when verify row is visible)
            if (VerifyRow.Visibility == Visibility.Visible)
            {
                var verify = pwdVerify.Password ?? string.Empty;
                if (!string.Equals(pw, verify, StringComparison.Ordinal))
                {
                    error = "Passwords do not match.";
                    ShowVerifyError(error);
                    return false;
                }
            }

            // Policy check (use existing strength panel for tips + also show a red error line)
            UpdateStrengthPanelForPolicy();
            if (!SecurePassword.IsPasswordValid(pw, pw, out _))
            {
                error = "Password does not meet policy requirements.";
                ShowVerifyError(error);
                return false;
            }

            HideVerifyError();
            HideStrengthRow();
            return true;
        }

        private void ShowStrengthRow()
        {
            if (StrengthPanel.Visibility != Visibility.Visible)
                StrengthPanel.Visibility = Visibility.Visible;
        }

        private void HideStrengthRow()
        {
            if (StrengthPanel.Visibility != Visibility.Collapsed)
                StrengthPanel.Visibility = Visibility.Collapsed;

            PwStrengthBar.Value = 0;
            PwStrengthText.Text = string.Empty;
            PwTipsList.ItemsSource = null;
        }

        private void UpdateStrengthPanelForPolicy()
        {
            var pw = pwdPassword.Password ?? string.Empty;

            if (string.IsNullOrEmpty(pw))
            {
                HideStrengthRow();
                return;
            }

            int classes = 0;
            if (pw.Any(char.IsLower)) classes++;
            if (pw.Any(char.IsUpper)) classes++;
            if (pw.Any(char.IsDigit)) classes++;
            if (pw.Any(ch => !char.IsLetterOrDigit(ch))) classes++;

            bool lengthOk = pw.Length >= 8;
            bool policyPass = lengthOk && classes >= 3;

            if (policyPass)
            {
                HideStrengthRow();
                return;
            }

            ShowStrengthRow();

            int score = (lengthOk ? 1 : 0) + classes;
            if (score > 4) score = 4;

            PwStrengthBar.Value = score / 4.0;

            string label = score switch
            {
                0 or 1 => "Very weak",
                2 => "Weak",
                3 => "Good",
                4 => "Strong",
                _ => "Weak"
            };
            PwStrengthText.Text = $"Password strength: {label} ({pw.Length} chars)";

            var brushKey = score switch
            {
                0 or 1 => "PwWeak",
                2 => "PwFair",
                3 => "PwStrong",
                4 => "PwVeryStrong",
                _ => "PwWeak"
            };
            if (TryFindResource(brushKey) is Brush b)
                PwStrengthBar.Foreground = b;

            var tips = new List<string>();
            if (!lengthOk) tips.Add("Use at least 8 characters.");
            if (!pw.Any(char.IsLower)) tips.Add("Add a lowercase letter.");
            if (!pw.Any(char.IsUpper)) tips.Add("Add an uppercase letter.");
            if (!pw.Any(char.IsDigit)) tips.Add("Add a digit.");
            if (!pw.Any(ch => !char.IsLetterOrDigit(ch))) tips.Add("Add a special character.");

            PwTipsList.ItemsSource = tips;
        }

        private void EnsureVerifyRowVisibleForManual()
        {
            if (VerifyRow.Visibility == Visibility.Visible)
                return;

            ShowVerifyRow();
        }

        private void ShowVerifyRow()
        {
            VerifyRow.Visibility = Visibility.Visible;
            lblVerifyPassword.Visibility = Visibility.Visible;

            UICleaner.Clear(pwdVerify);
            UICleaner.Clear(txtVerifyPlain);

            HideVerifyPassword();
            HideVerifyError();

            btnToggleVerifyReveal.Visibility = Visibility.Collapsed;
        }

        private void HideVerifyRow()
        {
            VerifyRow.Visibility = Visibility.Collapsed;
            lblVerifyPassword.Visibility = Visibility.Collapsed;

            UICleaner.Clear(pwdVerify);
            UICleaner.Clear(txtVerifyPlain);

            HideVerifyPassword();
            HideVerifyError();

            btnToggleVerifyReveal.Visibility = Visibility.Collapsed;
        }

        private void EvaluateAndDisplayVerifyMismatch()
        {
            if (chkBookmarkOnly.IsChecked == true)
            {
                HideVerifyError();
                return;
            }

            if (VerifyRow.Visibility != Visibility.Visible)
            {
                HideVerifyError();
                return;
            }

            var pw = pwdPassword.Password ?? string.Empty;
            var verify = pwdVerify.Password ?? string.Empty;

            if (!string.IsNullOrEmpty(verify) && !string.Equals(pw, verify, StringComparison.Ordinal))
                ShowVerifyError("Passwords do not match.");
            else
                HideVerifyError();
        }

        private void ShowVerifyError(string message)
        {
            VerifyErrorText.Text = message ?? string.Empty;

            if (VerifyErrorPanel.Visibility != Visibility.Visible)
                VerifyErrorPanel.Visibility = Visibility.Visible;
        }

        private void HideVerifyError()
        {
            VerifyErrorText.Text = string.Empty;

            if (VerifyErrorPanel.Visibility != Visibility.Collapsed)
                VerifyErrorPanel.Visibility = Visibility.Collapsed;
        }

        /* ======================= Email validation ======================= */

        private void txtEmail_LostFocus(object? sender, RoutedEventArgs e)
        {
            var s = (txtEmail.Text ?? string.Empty).Trim();

            if (s.Length == 0)
            {
                ClearEmailValidation();
                _lastEmailChecked = string.Empty;
                return;
            }

            if (string.Equals(s, _lastEmailChecked, StringComparison.Ordinal))
                return;

            _lastEmailChecked = s;

            var result = EmailValidator.IsLikelyEmail(s, out var message);
            if (result == EmailCheck.Ok)
                MarkEmailValid();
            else
                MarkEmailInvalid(message);
        }

        private bool ValidateEmailForSubmit()
        {
            var s = (txtEmail.Text ?? string.Empty).Trim();

            // Only error if user entered something
            if (s.Length == 0)
            {
                ClearEmailValidation();
                _lastEmailChecked = string.Empty;
                return true;
            }

            var result = EmailValidator.IsLikelyEmail(s, out var message);
            if (result == EmailCheck.Ok)
            {
                MarkEmailValid();
                _lastEmailChecked = s;
                return true;
            }

            MarkEmailInvalid(message);
            _lastEmailChecked = s;
            return false;
        }

        private void MarkEmailValid()
        {
            EmailErrorText.Text = string.Empty;
            EmailErrorPanel.Visibility = Visibility.Collapsed;

            txtEmail.ToolTip = null;
            if (_emailDefaultBackground != null)
                txtEmail.Background = _emailDefaultBackground;
            if (_emailDefaultBorderBrush != null)
                txtEmail.BorderBrush = _emailDefaultBorderBrush;
        }

        private void MarkEmailInvalid(string message)
        {
            EmailErrorText.Text = string.IsNullOrWhiteSpace(message)
                ? "Please enter a valid email address."
                : message;

            if (EmailErrorPanel.Visibility != Visibility.Visible)
                EmailErrorPanel.Visibility = Visibility.Visible;

            txtEmail.ToolTip = EmailErrorText.Text;

            var fill = TryFindResource("FieldErrorFill") as Brush
                       ?? new SolidColorBrush(Color.FromRgb(0xFF, 0xEE, 0xEE));
            txtEmail.Background = fill;
        }

        private void ClearEmailValidation()
        {
            EmailErrorText.Text = string.Empty;
            EmailErrorPanel.Visibility = Visibility.Collapsed;

            txtEmail.ToolTip = null;
            if (_emailDefaultBackground != null)
                txtEmail.Background = _emailDefaultBackground;
            if (_emailDefaultBorderBrush != null)
                txtEmail.BorderBrush = _emailDefaultBorderBrush;
        }

        /* ======================= Phone validation + reveal ======================= */

        private void txtPhone_LostFocus(object sender, RoutedEventArgs e)
        {
            ValidatePhoneNumber(forSubmit: false);
        }

        private void txtPhone_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_phoneRevealed)
                txtPhonePlain.Text = txtPhone.Password;

            TouchRevealTimerIfNeeded();

            if (string.IsNullOrEmpty(txtPhone.Password))
                ClearPhoneError();
        }

        private void BtnTogglePhoneReveal_Click(object? sender, RoutedEventArgs e)
        {
            if (_phoneRevealed)
                HidePhone();
            else
                ShowPhone();

            TouchRevealTimerIfNeeded();
        }

        private void ShowPhone()
        {
            txtPhonePlain.Text = txtPhone.Password;
            txtPhonePlain.Visibility = Visibility.Visible;
            txtPhone.Visibility = Visibility.Collapsed;
            _phoneRevealed = true;
        }

        private void HidePhone()
        {
            UICleaner.Clear(txtPhonePlain);
            txtPhonePlain.Visibility = Visibility.Collapsed;
            txtPhone.Visibility = Visibility.Visible;
            _phoneRevealed = false;
        }

        private bool ValidatePhoneNumber(bool forSubmit)
        {
            var raw = txtPhone.Password ?? string.Empty;
            var digitsOnly = new string(raw.Where(char.IsDigit).ToArray());
            int len = digitsOnly.Length;

            // Only error if user entered something
            if (len == 0)
            {
                ClearPhoneError();
                return true;
            }

            if (len < 7)
            {
                ShowPhoneError("Phone number appears too short. Please enter at least 7 digits or clear the field.");
                return false;
            }

            ClearPhoneError();
            return true;
        }

        private void ShowPhoneError(string message)
        {
            PhoneErrorText.Text = message ?? string.Empty;
            PhoneErrorPanel.Visibility = Visibility.Visible;

            txtPhone.ToolTip = PhoneErrorText.Text;

            var fill = TryFindResource("FieldErrorFill") as Brush
                       ?? new SolidColorBrush(Color.FromRgb(0xFF, 0xEE, 0xEE));
            txtPhone.Background = fill;
        }

        private void ClearPhoneError()
        {
            PhoneErrorText.Text = string.Empty;
            PhoneErrorPanel.Visibility = Visibility.Collapsed;

            txtPhone.ToolTip = null;
            if (_phoneDefaultBackground != null)
                txtPhone.Background = _phoneDefaultBackground;
            if (_phoneDefaultBorderBrush != null)
                txtPhone.BorderBrush = _phoneDefaultBorderBrush;
        }

        /* ======================= Form reset / wipe ======================= */

        private void ClearForm()
        {
            WipeSensitiveFields();

            txtItemName.Text = string.Empty;
            txtUsername.Text = string.Empty;
            txtEmail.Text = string.Empty;
            txtUrl.Text = string.Empty;
            txtDescription.Text = string.Empty;

            _lastEmailChecked = string.Empty;

            ClearItemNameError();
            ClearEmailValidation();
            ClearPhoneError();
        }

        private void WipeSensitiveFields()
        {
            HideAllRevealsAndStopTimer();

            UICleaner.Clear(pwdPassword);
            UICleaner.Clear(pwdVerify);
            UICleaner.Clear(txtPhone);

            ClearPlainRevealOverlays();

            HideStrengthRow();
            HideVerifyError();
        }

        private void ClearPlainRevealOverlays()
        {
            UICleaner.Clear(txtPasswordPlain);
            UICleaner.Clear(txtVerifyPlain);
            UICleaner.Clear(txtPhonePlain);
        }

        private void ResetUiState()
        {
            HideAllRevealsAndStopTimer();
            HideStrengthRow();
            HideVerifyRow();
            HideVerifyError();

            ClearItemNameError();
            ClearEmailValidation();
            ClearPhoneError();
        }

        /* ======================= Load/Unload helpers ======================= */

        private void CacheDefaultFieldVisualsIfNeeded()
        {
            _itemNameDefaultBorderBrush ??= txtItemName.BorderBrush;
            _itemNameDefaultBackground ??= txtItemName.Background;

            _emailDefaultBorderBrush ??= txtEmail.BorderBrush;
            _emailDefaultBackground ??= txtEmail.Background;

            _phoneDefaultBorderBrush ??= txtPhone.BorderBrush;
            _phoneDefaultBackground ??= txtPhone.Background;
        }

        private void HookUiEventsOnce()
        {
            if (_uiEventsHooked)
                return;

            // Ensure no duplicates even if designer/XAML also wires
            txtEmail.LostFocus -= txtEmail_LostFocus;
            txtEmail.LostFocus += txtEmail_LostFocus;

            txtItemName.TextChanged -= txtItemName_TextChanged;
            txtItemName.TextChanged += txtItemName_TextChanged;

            chkBookmarkOnly.Checked -= chkBookmarkOnly_Changed;
            chkBookmarkOnly.Unchecked -= chkBookmarkOnly_Changed;
            chkBookmarkOnly.Checked += chkBookmarkOnly_Changed;
            chkBookmarkOnly.Unchecked += chkBookmarkOnly_Changed;

            _uiEventsHooked = true;
        }

        private void UnhookUiEvents()
        {
            if (!_uiEventsHooked)
                return;

            txtEmail.LostFocus -= txtEmail_LostFocus;
            txtItemName.TextChanged -= txtItemName_TextChanged;

            chkBookmarkOnly.Checked -= chkBookmarkOnly_Changed;
            chkBookmarkOnly.Unchecked -= chkBookmarkOnly_Changed;

            _uiEventsHooked = false;
        }

        private void txtItemName_TextChanged(object? sender, TextChangedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(txtItemName.Text))
                ClearItemNameError();
        }

        private void chkBookmarkOnly_Changed(object? sender, RoutedEventArgs e)
        {
            if (chkBookmarkOnly.IsChecked == true)
            {
                // Bookmark-only: keep UI clean and skip password validation signals.
                HideStrengthRow();
                HideVerifyRow();
                HideVerifyError();
            }
        }
    }
}
