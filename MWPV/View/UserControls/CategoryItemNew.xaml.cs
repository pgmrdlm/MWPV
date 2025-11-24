using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Security.Utility.Storage;
using Security.Utility.Validation;

namespace MWPV.View.UserControls
{
    public partial class CategoryItemNew : UserControl
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

        private readonly DispatcherTimer _revealTimer;

        private bool _settingPwProgrammatically;
        private bool _verifyRowShown;

        // Email visuals
        private Brush? _emailDefaultBorderBrush;
        private Brush? _emailDefaultBackground;
        private string _lastEmailChecked = string.Empty;

        // Phone visuals
        private Brush? _phoneDefaultBorderBrush;
        private Brush? _phoneDefaultBackground;

        public CategoryItemNew()
        {
            InitializeComponent();

            Loaded += CategoryItemNew_Loaded;
            Unloaded += CategoryItemNew_Unloaded;

            txtEmail.LostFocus += txtEmail_LostFocus;

            _revealTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(20)
            };
            _revealTimer.Tick += RevealTimer_Tick;
        }

        private void CategoryItemNew_Loaded(object? sender, RoutedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[ITEM-NEW] Loaded");
#endif
            _emailDefaultBorderBrush = txtEmail.BorderBrush;
            _emailDefaultBackground = txtEmail.Background;

            _phoneDefaultBorderBrush = txtPhone.BorderBrush;
            _phoneDefaultBackground = txtPhone.Background;

            ClearForm();
            HideAllRevealsAndStopTimer();
            HideStrengthRow();
            HideVerifyRow();
            HideVerifyError();
            ClearEmailValidation();
            ClearPhoneError();
        }

        private void CategoryItemNew_Unloaded(object? sender, RoutedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[ITEM-NEW] Unloaded");
#endif
            HideAllRevealsAndStopTimer();
            WipeSensitiveFields();
            HideStrengthRow();
            HideVerifyRow();
            HideVerifyError();
            ClearEmailValidation();
            ClearPhoneError();
        }

        public void ConfigureForAdd(int categoryKey, string categoryName)
        {
            _categoryKey = categoryKey;
            _categoryName = categoryName;
            _isEditMode = false;
#if DEBUG
            Debug.WriteLine($"[ITEM-NEW] ConfigureForAdd: key={categoryKey}, name='{categoryName}'");
#endif
            ClearForm();
        }

        public void ConfigureForEdit(int categoryKey, string categoryName, object existingItem)
        {
            _categoryKey = categoryKey;
            _categoryName = categoryName;
            _isEditMode = true;
#if DEBUG
            Debug.WriteLine($"[ITEM-NEW] ConfigureForEdit: key={categoryKey}, name='{categoryName}'");
#endif
            // TODO: Map existingItem -> fields (and into BankAndSecurityPanel) once persistence is wired
        }

        /* ======================= Submit / Cancel ======================= */

        private void btnSubmit_Click(object? sender, RoutedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[ITEM-NEW] Submit clicked");
#endif
            if (string.IsNullOrWhiteSpace(txtItemName.Text))
            {
                MessageBox.Show("Item name is required.",
                                "Validation",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                return;
            }

            if (!ValidatePasswordForSubmission(out var error))
            {
                PasswordValidationFailed?.Invoke(this, error);
                if (VerifyRow.Visibility == Visibility.Visible &&
                    !string.IsNullOrEmpty(pwdVerify.Password))
                    pwdVerify.Focus();
                else
                    pwdPassword.Focus();
                return;
            }

            if (!ValidatePhoneNumber(forSubmit: true))
            {
                txtPhone.Focus();
                return;
            }

            try
            {
                Submitted?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error saving item.\n\n" + ex.Message,
                                "Error",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
            }
        }

        private void btnCancel_Click(object? sender, RoutedEventArgs e)
        {
            HideAllRevealsAndStopTimer();
            WipeSensitiveFields();
            HideStrengthRow();
            HideVerifyRow();
            HideVerifyError();
            ClearEmailValidation();
            ClearPhoneError();

            Canceled?.Invoke(this, EventArgs.Empty);
        }

        /* ======================= Shared reveal timer ======================= */

        private void RevealTimer_Tick(object? sender, EventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[ITEM-NEW] Reveal timer elapsed – hiding all reveals");
#endif
            HideAllRevealsAndStopTimer();
        }

        private void HideAllRevealsAndStopTimer()
        {
            _revealTimer.Stop();
            HideMainPassword();
            HideVerifyPassword();
            HidePhone();
        }

        private void StartOrRestartRevealTimerIfNeeded()
        {
            _revealTimer.Stop();

            if (_mainRevealed ||
                _verifyRevealed ||
                _phoneRevealed)
            {
                _revealTimer.Start();
            }
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

            StartOrRestartRevealTimerIfNeeded();
        }

        private void BtnToggleVerifyReveal_Click(object? sender, RoutedEventArgs e)
        {
            if (_verifyRevealed)
                HideVerifyPassword();
            else
                ShowVerifyPassword();

            StartOrRestartRevealTimerIfNeeded();
        }

        private void PwdPassword_PreviewKeyDown(object? sender, KeyEventArgs e)
        {
            if (_settingPwProgrammatically) return;

            bool pasteCombo =
                (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) ||
                (e.Key == Key.Insert && (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift);

            if (pasteCombo)
                _verifyRowShown = false;
        }

        private void pwdPassword_PasswordChanged(object? sender, RoutedEventArgs e)
        {
            if (_mainRevealed)
                txtPasswordPlain.Text = pwdPassword.Password;

            if (_mainRevealed || _verifyRevealed || _phoneRevealed)
                StartOrRestartRevealTimerIfNeeded();

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
            if (!string.IsNullOrEmpty(pwdPassword.Password))
                UpdateStrengthPanelForPolicy();
        }

        private void pwdVerify_PasswordChanged(object? sender, RoutedEventArgs e)
        {
            if (_verifyRevealed)
                txtVerifyPlain.Text = pwdVerify.Password;

            if (_mainRevealed || _verifyRevealed || _phoneRevealed)
                StartOrRestartRevealTimerIfNeeded();

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
            if (!string.IsNullOrEmpty(txtPasswordPlain.Text))
                txtPasswordPlain.Text = string.Empty;

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
            if (!string.IsNullOrEmpty(txtVerifyPlain.Text))
                txtVerifyPlain.Text = string.Empty;

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

        private bool ValidatePasswordForSubmission(out string error)
        {
            error = string.Empty;
            var pw = pwdPassword.Password ?? string.Empty;

            if (string.IsNullOrEmpty(pw))
            {
                HideVerifyError();
                return true;
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

            if (!SecurePassword.IsPasswordValid(pw, pw, out _))
            {
                HideVerifyError();
                return false;
            }

            HideVerifyError();
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
            if (_verifyRowShown) return;
            ShowVerifyRow();
        }

        private void ShowVerifyRow()
        {
            VerifyRow.Visibility = Visibility.Visible;
            _verifyRowShown = true;
            pwdVerify.Password = string.Empty;
            txtVerifyPlain.Text = string.Empty;
            HideVerifyPassword();
            HideVerifyError();
        }

        private void HideVerifyRow()
        {
            VerifyRow.Visibility = Visibility.Collapsed;
            _verifyRowShown = false;
            pwdVerify.Password = string.Empty;
            txtVerifyPlain.Text = string.Empty;
            HideVerifyPassword();
            HideVerifyError();
        }

        private void EvaluateAndDisplayVerifyMismatch()
        {
            if (VerifyRow.Visibility != Visibility.Visible)
            {
                HideVerifyError();
                return;
            }

            var pw = pwdPassword.Password ?? string.Empty;
            var verify = pwdVerify.Password ?? string.Empty;

            if (!string.IsNullOrEmpty(verify) &&
                !string.Equals(pw, verify, StringComparison.Ordinal))
            {
                ShowVerifyError("Passwords do not match.");
            }
            else
            {
                HideVerifyError();
            }
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

        /* ======================= Email LostFocus validation ======================= */

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

        private void MarkEmailValid()
        {
            try
            {
                EmailErrorText.Text = string.Empty;
                EmailErrorPanel.Visibility = Visibility.Collapsed;

                txtEmail.ToolTip = null;
                if (_emailDefaultBackground != null)
                    txtEmail.Background = _emailDefaultBackground;
                if (_emailDefaultBorderBrush != null)
                    txtEmail.BorderBrush = _emailDefaultBorderBrush;
            }
            catch { }
        }

        private void MarkEmailInvalid(string message)
        {
            try
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
            catch { }
        }

        private void ClearEmailValidation()
        {
            try
            {
                EmailErrorText.Text = string.Empty;
                EmailErrorPanel.Visibility = Visibility.Collapsed;

                txtEmail.ToolTip = null;
                if (_emailDefaultBackground != null)
                    txtEmail.Background = _emailDefaultBackground;
                if (_emailDefaultBorderBrush != null)
                    txtEmail.BorderBrush = _emailDefaultBorderBrush;
            }
            catch { }
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

            if (_mainRevealed || _verifyRevealed || _phoneRevealed)
                StartOrRestartRevealTimerIfNeeded();

            if (string.IsNullOrEmpty(txtPhone.Password))
                ClearPhoneError();
        }

        private void BtnTogglePhoneReveal_Click(object? sender, RoutedEventArgs e)
        {
            if (_phoneRevealed)
                HidePhone();
            else
                ShowPhone();

            StartOrRestartRevealTimerIfNeeded();
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
            if (!string.IsNullOrEmpty(txtPhonePlain.Text))
                txtPhonePlain.Text = string.Empty;

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
                var msg = "Phone number appears too short. Please enter at least 7 digits or clear the field.";
                ShowPhoneError(msg);
                return false;
            }

            ClearPhoneError();
            return true;
        }

        private void ShowPhoneError(string message)
        {
            try
            {
                PhoneErrorText.Text = message ?? string.Empty;
                PhoneErrorPanel.Visibility = Visibility.Visible;

                txtPhone.ToolTip = PhoneErrorText.Text;

                var fill = TryFindResource("FieldErrorFill") as Brush
                           ?? new SolidColorBrush(Color.FromRgb(0xFF, 0xEE, 0xEE));
                txtPhone.Background = fill;
            }
            catch { }
        }

        private void ClearPhoneError()
        {
            try
            {
                PhoneErrorText.Text = string.Empty;
                PhoneErrorPanel.Visibility = Visibility.Collapsed;

                txtPhone.ToolTip = null;
                if (_phoneDefaultBackground != null)
                    txtPhone.Background = _phoneDefaultBackground;
                if (_phoneDefaultBorderBrush != null)
                    txtPhone.BorderBrush = _phoneDefaultBorderBrush;
            }
            catch { }
        }

        /* ======================= Form reset / wipe ======================= */

        private void ClearForm()
        {
            try
            {
                WipeSensitiveFields();

                txtItemName.Text = string.Empty;
                txtUsername.Text = string.Empty;
                txtEmail.Text = string.Empty;
                txtUrl.Text = string.Empty;
                txtDescription.Text = string.Empty;

                EmailErrorText.Text = string.Empty;
                EmailErrorPanel.Visibility = Visibility.Collapsed;

                PhoneErrorText.Text = string.Empty;
                PhoneErrorPanel.Visibility = Visibility.Collapsed;

                HideStrengthRow();
                HideVerifyRow();
            }
            catch
            {
            }
        }

        private void WipeSensitiveFields()
        {
            try
            {
                HideAllRevealsAndStopTimer();

                pwdPassword.Password = string.Empty;
                pwdVerify.Password = string.Empty;

                txtPhone.Password = string.Empty;

                txtPasswordPlain.Text = string.Empty;
                txtVerifyPlain.Text = string.Empty;
                txtPhonePlain.Text = string.Empty;

                HideStrengthRow();
                HideVerifyError();
            }
            catch
            {
            }
        }
    }
}
