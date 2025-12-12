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
using MWPV.Utilities.UI; // <-- UICleaner

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
            // Make absolutely sure the helper isn't still running after we leave.
            _revealAutoHide.Stop();

            UnhookUiEvents();

            // Full exit cleanup: wipe everything sensitive.
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
            if (string.IsNullOrWhiteSpace(txtItemName.Text))
            {
                // TODO: Replace MessageBox with app-standard dialog / ErrorHandler.
                MessageBox.Show("Item name is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!ValidatePasswordForSubmission(out var error))
            {
                PasswordValidationFailed?.Invoke(this, error);

                if (VerifyRow.Visibility == Visibility.Visible && !string.IsNullOrEmpty(pwdVerify.Password))
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
                // TODO: Replace MessageBox with app-standard dialog / ErrorHandler.
                MessageBox.Show("Error saving item.\n\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            // Timer behavior: remask + wipe ONLY the plain overlays (don’t destroy user-entered PB values).
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
            UICleaner.Clear(txtPasswordPlain); // plain overlay wipe
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
            UICleaner.Clear(txtVerifyPlain); // plain overlay wipe
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
            UICleaner.Clear(txtPhonePlain); // plain overlay wipe
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
        }

        /// <summary>
        /// Full wipe (exit/cancel/unload): clears PasswordBoxes + plain overlays using UICleaner.
        /// </summary>
        private void WipeSensitiveFields()
        {
            HideAllRevealsAndStopTimer();

            // PasswordBoxes
            UICleaner.Clear(pwdPassword);
            UICleaner.Clear(pwdVerify);
            UICleaner.Clear(txtPhone);

            // Plain overlays
            ClearPlainRevealOverlays();

            HideStrengthRow();
            HideVerifyError();
        }

        /// <summary>
        /// Timer / hide wipe: clears ONLY the plain overlays (best for “remask” behavior).
        /// </summary>
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
            ClearEmailValidation();
            ClearPhoneError();
        }

        /* ======================= Load/Unload helpers ======================= */

        private void CacheDefaultFieldVisualsIfNeeded()
        {
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

            _uiEventsHooked = true;
        }

        private void UnhookUiEvents()
        {
            if (!_uiEventsHooked)
                return;

            txtEmail.LostFocus -= txtEmail_LostFocus;
            _uiEventsHooked = false;
        }
    }
}
