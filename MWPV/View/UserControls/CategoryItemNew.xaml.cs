using System;
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

        private bool _mainRevealed;
        private bool _verifyRevealed;
        private readonly DispatcherTimer _revealTimer;

        private bool _settingPwProgrammatically;
        private bool _verifyRowShown;

        // Email visuals
        private Brush? _emailDefaultBorderBrush;
        private Brush? _emailDefaultBackground;
        private string _lastEmailChecked = string.Empty;

        public CategoryItemNew()
        {
            InitializeComponent();

            Loaded += CategoryItemNew_Loaded;
            Unloaded += CategoryItemNew_Unloaded;

            pwdPassword.PreviewKeyDown += PwdPassword_PreviewKeyDown;
            txtEmail.LostFocus += txtEmail_LostFocus;

            _revealTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            _revealTimer.Tick += (_, __) =>
            {
                HideMainPassword();
                HideVerifyPassword();
            };
        }

        private void CategoryItemNew_Loaded(object? sender, RoutedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[ITEM-EDIT] Loaded");
#endif
            _emailDefaultBorderBrush = txtEmail.BorderBrush;
            _emailDefaultBackground = txtEmail.Background;

            ClearForm();
            HideMainPassword();
            HideVerifyPassword();
            HideStrengthRow();
            HideVerifyRow();
            HideVerifyError();
            ClearEmailValidation();
        }

        private void CategoryItemNew_Unloaded(object? sender, RoutedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[ITEM-EDIT] Unloaded");
#endif
            _revealTimer.Stop();
            WipeSensitiveFields();
            HideStrengthRow();
            HideVerifyRow();
            HideVerifyError();
            ClearEmailValidation();
        }

        public void ConfigureForAdd(int categoryKey, string categoryName)
        {
            _categoryKey = categoryKey;
            _categoryName = categoryName;
            _isEditMode = false;
#if DEBUG
            Debug.WriteLine($"[ITEM-EDIT] ConfigureForAdd: key={categoryKey}, name='{categoryName}'");
#endif
            ClearForm();
        }

        public void ConfigureForEdit(int categoryKey, string categoryName, object existingItem)
        {
            _categoryKey = categoryKey;
            _categoryName = categoryName;
            _isEditMode = true;
#if DEBUG
            Debug.WriteLine($"[ITEM-EDIT] ConfigureForEdit: key={categoryKey}, name='{categoryName}'");
#endif
            // TODO: Map existingItem -> fields
        }

        private void btnSubmit_Click(object? sender, RoutedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[ITEM-EDIT] Submit clicked");
#endif
            if (string.IsNullOrWhiteSpace(txtItemName.Text))
            {
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

            try
            {
                Submitted?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error saving item.\n\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnCancel_Click(object? sender, RoutedEventArgs e)
        {
            _revealTimer.Stop();
            HideMainPassword();
            HideVerifyPassword();
            WipeSensitiveFields();
            HideStrengthRow();
            HideVerifyRow();
            HideVerifyError();
            ClearEmailValidation();
            Canceled?.Invoke(this, EventArgs.Empty);
        }

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
            => (_mainRevealed ? (Action)HideMainPassword : ShowMainPassword)();

        private void BtnToggleVerifyReveal_Click(object? sender, RoutedEventArgs e)
            => (_verifyRevealed ? (Action)HideVerifyPassword : ShowVerifyPassword)();

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
                if (_emailDefaultBackground != null) txtEmail.Background = _emailDefaultBackground;
                if (_emailDefaultBorderBrush != null) txtEmail.BorderBrush = _emailDefaultBorderBrush;
            }
            catch { }
        }

        private void MarkEmailInvalid(string message)
        {
            try
            {
                EmailErrorText.Text = string.IsNullOrWhiteSpace(message) ? "Please enter a valid email address." : message;
                if (EmailErrorPanel.Visibility != Visibility.Visible)
                    EmailErrorPanel.Visibility = Visibility.Visible;

                txtEmail.ToolTip = EmailErrorText.Text;

                var fill = TryFindResource("FieldErrorFill") as Brush
                           ?? new SolidColorBrush(Color.FromRgb(0xFF, 0xEE, 0xEE));
                txtEmail.Background = fill;

                // keep border as-is (easier on the eyes)
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
                if (_emailDefaultBackground != null) txtEmail.Background = _emailDefaultBackground;
                if (_emailDefaultBorderBrush != null) txtEmail.BorderBrush = _emailDefaultBorderBrush;
            }
            catch { }
        }

        /* ======================= Password Helpers (unchanged) ======================= */

        private void PwdPassword_PreviewKeyDown(object? sender, KeyEventArgs e)
        {
            if (_settingPwProgrammatically) return;

            bool pasteCombo =
                (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) ||
                (e.Key == Key.Insert && (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift);

            if (pasteCombo) _verifyRowShown = false;
        }

        private void pwdPassword_PasswordChanged(object? sender, RoutedEventArgs e)
        {
            if (_revealTimer.IsEnabled) { _revealTimer.Stop(); _revealTimer.Start(); }

            if (_mainRevealed)
                txtPasswordPlain.Text = pwdPassword.Password;

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
            if (_revealTimer.IsEnabled) { _revealTimer.Stop(); _revealTimer.Start(); }
            if (_verifyRevealed) txtVerifyPlain.Text = pwdVerify.Password;
            EvaluateAndDisplayVerifyMismatch();
        }

        private void ShowMainPassword()
        {
            txtPasswordPlain.Text = pwdPassword.Password;
            txtPasswordPlain.Visibility = Visibility.Visible;
            pwdPassword.Visibility = Visibility.Collapsed;
            _mainRevealed = true;
            _revealTimer.Stop(); _revealTimer.Start();
        }

        private void HideMainPassword()
        {
            if (!string.IsNullOrEmpty(txtPasswordPlain.Text)) txtPasswordPlain.Text = string.Empty;
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
            _revealTimer.Stop(); _revealTimer.Start();
        }

        private void HideVerifyPassword()
        {
            if (!string.IsNullOrEmpty(txtVerifyPlain.Text)) txtVerifyPlain.Text = string.Empty;
            txtVerifyPlain.Visibility = Visibility.Collapsed;
            pwdVerify.Visibility = Visibility.Visible;
            _verifyRevealed = false;
        }

        private void SetPassword(string value)
        {
            pwdPassword.Password = value;
            if (_mainRevealed) txtPasswordPlain.Text = value;
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

            if (!Security.Utility.Storage.SecurePassword.IsPasswordValid(pw, pw, out var _))
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

            if (string.IsNullOrEmpty(pw)) { HideStrengthRow(); return; }

            int classes = 0;
            if (pw.Any(char.IsLower)) classes++;
            if (pw.Any(char.IsUpper)) classes++;
            if (pw.Any(char.IsDigit)) classes++;
            if (pw.Any(ch => !char.IsLetterOrDigit(ch))) classes++;

            bool lengthOk = pw.Length >= 8;
            bool policyPass = lengthOk && classes >= 3;

            if (policyPass) { HideStrengthRow(); return; }

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

            var tips = new System.Collections.Generic.List<string>();
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
            if (VerifyRow.Visibility != Visibility.Visible) { HideVerifyError(); return; }

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

        private void ClearForm()
        {
            txtItemName.Text = string.Empty;
            txtUrl.Text = string.Empty;

            pwdPassword.Password = string.Empty;
            txtPasswordPlain.Text = string.Empty;
            txtPasswordPlain.Visibility = Visibility.Collapsed;
            _mainRevealed = false;

            pwdVerify.Password = string.Empty;
            txtVerifyPlain.Text = string.Empty;
            txtVerifyPlain.Visibility = Visibility.Collapsed;
            _verifyRevealed = false;

            txtUsername.Text = string.Empty;
            txtEmail.Text = string.Empty;
            txtPhone.Text = string.Empty;
            txtSecQuestion.Text = string.Empty;
            txtSecAnswer.Text = string.Empty;

            txtDescription.Text = string.Empty;
            txtExpDate.Text = string.Empty;
            txtCvv.Text = string.Empty;
            txtAccountNumber.Text = string.Empty;
            txtPin.Text = string.Empty;

            cboCardType.SelectedIndex = -1;
            cboAccountName.SelectedIndex = -1;

            HideStrengthRow();
            HideVerifyRow();
            HideVerifyError();
            ClearEmailValidation();
            _lastEmailChecked = string.Empty;
        }

        private void WipeSensitiveFields()
        {
            try
            {
                HideMainPassword();
                HideVerifyPassword();
                pwdPassword.Password = string.Empty;
                pwdVerify.Password = string.Empty;
                txtCvv.Text = string.Empty;
                txtPin.Text = string.Empty;
                HideStrengthRow();
                HideVerifyError();
            }
            catch { }
        }
    }
}
