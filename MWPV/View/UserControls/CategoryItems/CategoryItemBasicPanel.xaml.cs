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
using MWPV.Utilities.UI;

namespace MWPV.View.UserControls.CategoryItems
{
    public partial class CategoryItemBasicPanel : UserControl
    {
        public event EventHandler? SaveRequested;
        public event EventHandler? CancelRequested;
        public event EventHandler<string>? PasswordValidationFailed;

        // Reveal state flags
        private bool _mainRevealed;
        private bool _verifyRevealed;
        private bool _phoneRevealed;
        private bool _pinRevealed;
        private bool _emailRevealed;

        // Shared reveal auto-hide
        private readonly AutoHideTimer _revealAutoHide;

        private bool _settingPwProgrammatically;

        // Default visuals
        private Brush? _emailDefaultBorderBrush;
        private Brush? _emailDefaultBackground;
        private Brush? _phoneDefaultBorderBrush;
        private Brush? _phoneDefaultBackground;
        private Brush? _itemNameDefaultBorderBrush;
        private Brush? _itemNameDefaultBackground;
        private Brush? _pinDefaultBorderBrush;
        private Brush? _pinDefaultBackground;

        private string _lastEmailChecked = string.Empty;

        // Prevent event stacking
        private bool _uiEventsHooked;

        // PIN rules
        private const int PinMinLen = 4;
        private const int PinMaxLen = 6;

        public CategoryItemBasicPanel()
        {
            InitializeComponent();

            Loaded += CategoryItemBasicPanel_Loaded;
            Unloaded += CategoryItemBasicPanel_Unloaded;

            _revealAutoHide = new AutoHideTimer(
                interval: TimeSpan.FromSeconds(20),
                onTimeout: OnRevealTimeout
            );
        }

        /* ======================= Lifecycle ======================= */

        private void CategoryItemBasicPanel_Loaded(object? sender, RoutedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[BASIC] Loaded");
#endif
            CacheDefaultFieldVisualsIfNeeded();
            HookUiEventsOnce();

            ClearForm();
            ResetUiState();
        }

        private void CategoryItemBasicPanel_Unloaded(object? sender, RoutedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[BASIC] Unloaded");
#endif
            _revealAutoHide.Stop();
            UnhookUiEvents();

            ResetUiState();
            WipeSensitiveFields();
        }

        /* ======================= Host-facing API ======================= */

        public void ClearForm()
        {
            WipeSensitiveFields();

            txtItemName.Text = string.Empty;
            txtUsername.Text = string.Empty;
            txtUrl.Text = string.Empty;
            txtDescription.Text = string.Empty;

            _lastEmailChecked = string.Empty;

            ClearItemNameError();
            ClearEmailValidation();
            ClearPhoneError();
            ClearPinError();
        }

        public void ResetUiState()
        {
            HideAllRevealsAndStopTimer();
            HideStrengthRow();
            HideVerifyRow();
            HideVerifyError();

            ClearItemNameError();
            ClearEmailValidation();
            ClearPhoneError();
            ClearPinError();
        }

        public void WipeSensitiveFields()
        {
            HideAllRevealsAndStopTimer();

            UICleaner.Clear(pwdPassword);
            UICleaner.Clear(pwdVerify);
            UICleaner.Clear(txtPhone);
            UICleaner.Clear(pwdPin);
            UICleaner.Clear(pwdEmail);

            ClearPlainRevealOverlays();

            HideStrengthRow();
            HideVerifyError();
        }

        public void WipeAllForHostClose()
        {
#if DEBUG
            Debug.WriteLine("[BASIC] WipeAllForHostClose");
#endif
            ResetUiState();
            WipeSensitiveFields();
        }

        public void NormalizeUsernameFromEmailIfEmpty()
        {
            var user = (txtUsername.Text ?? string.Empty).Trim();
            if (user.Length != 0)
                return;

            var email = (pwdEmail.Password ?? string.Empty).Trim();
            if (email.Length == 0)
                return;

            var result = EmailValidator.IsLikelyEmail(email, out _);
            if (result != EmailCheck.Ok)
                return;

            txtUsername.Text = email;
        }

        public bool TryValidateAllForSubmit(out bool isBookmarkOnly, out bool okName, out bool okPassword, out bool okPin, out bool okEmail, out bool okPhone)
        {
            isBookmarkOnly = chkBookmarkOnly.IsChecked == true;

            okName = ValidateItemName(forSubmit: true);

            // Password required unless Bookmark-only
            okPassword = ValidatePasswordForSubmission(isBookmarkOnly, out _);

            // PIN optional; if present must be valid
            okPin = ValidatePin(forSubmit: true);

            // Email / Phone only error if user entered a value
            okEmail = ValidateEmailForSubmit();
            okPhone = ValidatePhoneNumber(forSubmit: true);

            return okName && okPassword && okPin && okEmail && okPhone;
        }

        public void FocusFirstError(bool okName, bool okPassword, bool okPin, bool okEmail, bool okPhone, bool isBookmarkOnly)
        {
            if (!okName)
            {
                txtItemName.Focus();
                return;
            }

            if (!okPassword)
            {
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

            if (!okPin)
            {
                pwdPin.Focus();
                return;
            }

            if (!okEmail)
            {
                pwdEmail.Focus();
                return;
            }

            if (!okPhone)
                txtPhone.Focus();
        }

        /* ======================= Save / Cancel (raise to host) ======================= */

        private void btnSubmit_Click(object? sender, RoutedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[BASIC] Save clicked");
#endif
            SaveRequested?.Invoke(this, EventArgs.Empty);
        }

        private void btnCancel_Click(object? sender, RoutedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[BASIC] Cancel clicked");
#endif
            CancelRequested?.Invoke(this, EventArgs.Empty);
        }

        /* ======================= Shared reveal auto-hide ======================= */

        private void OnRevealTimeout()
        {
#if DEBUG
            Debug.WriteLine("[BASIC] Reveal timer elapsed – hiding all reveals");
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
            HidePin();
            HideEmail();
        }

        private void TouchRevealTimerIfNeeded()
        {
            bool anyRevealed = _mainRevealed || _verifyRevealed || _phoneRevealed || _pinRevealed || _emailRevealed;
            _revealAutoHide.Touch(anyRevealed);
        }

        /* ======================= Item name validation ======================= */

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
            ItemNameErrorText.Text = message ?? string.Empty;
            ItemNameErrorPanel.Visibility = Visibility.Visible;

            txtItemName.ToolTip = message;

            var fill = TryFindResource("FieldErrorFill") as Brush
                       ?? new SolidColorBrush(Color.FromRgb(0xFF, 0xEE, 0xEE));
            txtItemName.Background = fill;
        }

        private void ClearItemNameError()
        {
            ItemNameErrorText.Text = string.Empty;
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
                try { SetPassword(generated); }
                finally { _settingPwProgrammatically = false; }

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
            if (_mainRevealed) HideMainPassword();
            else ShowMainPassword();

            TouchRevealTimerIfNeeded();
        }

        private void BtnToggleVerifyReveal_Click(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(pwdVerify.Password))
                return;

            if (_verifyRevealed) HideVerifyPassword();
            else ShowVerifyPassword();

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

            btnToggleVerifyReveal.Visibility = string.IsNullOrEmpty(pwdVerify.Password)
                ? Visibility.Collapsed
                : Visibility.Visible;

            if (string.IsNullOrEmpty(pwdVerify.Password))
                HideVerifyPassword();

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

            if (string.IsNullOrEmpty(pw))
            {
                HideStrengthRow();
                ShowVerifyError("Password is required.");
                return false;
            }

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
            VerifyErrorPanel.Visibility = Visibility.Visible;
        }

        private void HideVerifyError()
        {
            VerifyErrorText.Text = string.Empty;
            VerifyErrorPanel.Visibility = Visibility.Collapsed;
        }

        /* ======================= PIN ======================= */

        private void Pin_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Text))
            {
                e.Handled = true;
                return;
            }

            foreach (char c in e.Text)
            {
                if (!char.IsDigit(c))
                {
                    e.Handled = true;
                    return;
                }
            }

            if (sender is PasswordBox pb)
            {
                if (pb.Password.Length + e.Text.Length > PinMaxLen)
                {
                    e.Handled = true;
                    return;
                }
            }

            e.Handled = false;
        }

        private void Pin_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            bool ctrlPaste = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
                             (e.Key == Key.V || e.Key == Key.Insert);
            bool shiftInsert = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift &&
                               e.Key == Key.Insert;

            if (ctrlPaste || shiftInsert)
            {
                e.Handled = true;
                return;
            }
        }

        private void Pin_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (!e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText, true))
            {
                e.CancelCommand();
                return;
            }

            string paste = (e.SourceDataObject.GetData(DataFormats.UnicodeText) as string) ?? "";
            paste = paste.Trim();

            if (paste.Length == 0)
            {
                e.CancelCommand();
                return;
            }

            foreach (char c in paste)
            {
                if (!char.IsDigit(c))
                {
                    e.CancelCommand();
                    return;
                }
            }

            if (sender is not PasswordBox pb)
            {
                e.CancelCommand();
                return;
            }

            int remaining = PinMaxLen - (pb.Password?.Length ?? 0);
            if (remaining <= 0)
            {
                e.CancelCommand();
                return;
            }

            if (paste.Length > remaining)
            {
                paste = paste.Substring(0, remaining);
                e.DataObject = new DataObject(DataFormats.UnicodeText, paste);
            }
        }

        private void Pin_PasswordChanged(object? sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(pwdPin.Password) && pwdPin.Password.Length > PinMaxLen)
                pwdPin.Password = pwdPin.Password.Substring(0, PinMaxLen);

            if (_pinRevealed)
                txtPinPlain.Text = pwdPin.Password;

            TouchRevealTimerIfNeeded();

            if (string.IsNullOrEmpty(pwdPin.Password))
            {
                HidePin();
                ClearPinError();
            }
            else
            {
                ValidatePin(forSubmit: false);
            }
        }

        private void Pin_ToggleReveal_Click(object? sender, RoutedEventArgs e)
        {
            // Button always enabled now. If empty, do nothing.
            if (string.IsNullOrEmpty(pwdPin.Password))
                return;

            if (_pinRevealed) HidePin();
            else ShowPin();

            TouchRevealTimerIfNeeded();
        }

        private void ShowPin()
        {
            txtPinPlain.Text = pwdPin.Password;
            txtPinPlain.Visibility = Visibility.Visible;
            pwdPin.Visibility = Visibility.Collapsed;
            _pinRevealed = true;
        }

        private void HidePin()
        {
            UICleaner.Clear(txtPinPlain);
            txtPinPlain.Visibility = Visibility.Collapsed;
            pwdPin.Visibility = Visibility.Visible;
            _pinRevealed = false;
        }

        private bool ValidatePin(bool forSubmit)
        {
            var raw = pwdPin.Password ?? string.Empty;

            if (raw.Length == 0)
            {
                ClearPinError();
                return true;
            }

            if (!raw.All(char.IsDigit))
            {
                ShowPinError("PIN must contain digits only (0–9).");
                return false;
            }

            if (raw.Length < PinMinLen || raw.Length > PinMaxLen)
            {
                ShowPinError("PIN must be 4–6 digits.");
                return false;
            }

            ClearPinError();
            return true;
        }

        private void ShowPinError(string message)
        {
            PinErrorText.Text = message ?? string.Empty;
            PinErrorPanel.Visibility = Visibility.Visible;

            pwdPin.ToolTip = PinErrorText.Text;

            var fill = TryFindResource("FieldErrorFill") as Brush
                       ?? new SolidColorBrush(Color.FromRgb(0xFF, 0xEE, 0xEE));
            pwdPin.Background = fill;
        }

        private void ClearPinError()
        {
            PinErrorText.Text = string.Empty;
            PinErrorPanel.Visibility = Visibility.Collapsed;

            pwdPin.ToolTip = null;
            if (_pinDefaultBackground != null)
                pwdPin.Background = _pinDefaultBackground;
            if (_pinDefaultBorderBrush != null)
                pwdPin.BorderBrush = _pinDefaultBorderBrush;
        }

        /* ======================= Email validation + Username autofill + reveal ======================= */

        private void pwdEmail_PasswordChanged(object? sender, RoutedEventArgs e)
        {
            if (_emailRevealed)
                txtEmailPlain.Text = pwdEmail.Password;

            TouchRevealTimerIfNeeded();

            if (string.IsNullOrEmpty(pwdEmail.Password))
            {
                _lastEmailChecked = string.Empty;
                ClearEmailValidation();
                HideEmail();
            }
        }

        private void pwdEmail_LostFocus(object? sender, RoutedEventArgs e)
        {
            var s = (pwdEmail.Password ?? string.Empty).Trim();

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
            {
                MarkEmailValid();
                NormalizeUsernameFromEmailIfEmpty();
            }
            else
            {
                MarkEmailInvalid(message);
            }
        }

        private void BtnToggleEmailReveal_Click(object? sender, RoutedEventArgs e)
        {
            if (_emailRevealed) HideEmail();
            else ShowEmail();

            TouchRevealTimerIfNeeded();
        }

        private void ShowEmail()
        {
            txtEmailPlain.Text = pwdEmail.Password;
            txtEmailPlain.Visibility = Visibility.Visible;
            pwdEmail.Visibility = Visibility.Collapsed;
            _emailRevealed = true;
        }

        private void HideEmail()
        {
            UICleaner.Clear(txtEmailPlain);
            txtEmailPlain.Visibility = Visibility.Collapsed;
            pwdEmail.Visibility = Visibility.Visible;
            _emailRevealed = false;
        }

        private bool ValidateEmailForSubmit()
        {
            var s = (pwdEmail.Password ?? string.Empty).Trim();

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

            pwdEmail.ToolTip = null;
            if (_emailDefaultBackground != null)
                pwdEmail.Background = _emailDefaultBackground;
            if (_emailDefaultBorderBrush != null)
                pwdEmail.BorderBrush = _emailDefaultBorderBrush;
        }

        private void MarkEmailInvalid(string message)
        {
            EmailErrorText.Text = string.IsNullOrWhiteSpace(message)
                ? "Please enter a valid email address."
                : message;

            EmailErrorPanel.Visibility = Visibility.Visible;

            pwdEmail.ToolTip = EmailErrorText.Text;

            var fill = TryFindResource("FieldErrorFill") as Brush
                       ?? new SolidColorBrush(Color.FromRgb(0xFF, 0xEE, 0xEE));
            pwdEmail.Background = fill;
        }

        private void ClearEmailValidation()
        {
            EmailErrorText.Text = string.Empty;
            EmailErrorPanel.Visibility = Visibility.Collapsed;

            pwdEmail.ToolTip = null;
            if (_emailDefaultBackground != null)
                pwdEmail.Background = _emailDefaultBackground;
            if (_emailDefaultBorderBrush != null)
                pwdEmail.BorderBrush = _emailDefaultBorderBrush;
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
            if (_phoneRevealed) HidePhone();
            else ShowPhone();

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

        /* ======================= Internal helpers ======================= */

        private void ClearPlainRevealOverlays()
        {
            UICleaner.Clear(txtPasswordPlain);
            UICleaner.Clear(txtVerifyPlain);
            UICleaner.Clear(txtPhonePlain);
            UICleaner.Clear(txtPinPlain);
            UICleaner.Clear(txtEmailPlain);
        }

        private void CacheDefaultFieldVisualsIfNeeded()
        {
            _itemNameDefaultBorderBrush ??= txtItemName.BorderBrush;
            _itemNameDefaultBackground ??= txtItemName.Background;

            _emailDefaultBorderBrush ??= pwdEmail.BorderBrush;
            _emailDefaultBackground ??= pwdEmail.Background;

            _phoneDefaultBorderBrush ??= txtPhone.BorderBrush;
            _phoneDefaultBackground ??= txtPhone.Background;

            _pinDefaultBorderBrush ??= pwdPin.BorderBrush;
            _pinDefaultBackground ??= pwdPin.Background;
        }

        private void HookUiEventsOnce()
        {
            if (_uiEventsHooked)
                return;

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
                HideStrengthRow();
                HideVerifyRow();
                HideVerifyError();
            }
        }
    }
}
