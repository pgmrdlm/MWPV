// File: MWPV/View/UserControls/CategoryitemEditNew.xaml.cs
using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Security.Utility.Storage; // SecurePassword (common generator/validator)

namespace MWPV.View.UserControls
{
    public partial class CategoryitemEditNew : UserControl
    {
        // Events raised back to the Panel overlay host
        public event EventHandler? Submitted;
        public event EventHandler? Canceled;

        // Raised when password validation fails; payload = human-readable error.
        public event EventHandler<string>? PasswordValidationFailed;

        private int _categoryKey;
        private string _categoryName = string.Empty;
        private bool _isEditMode;

        // Reveal state
        private bool _mainRevealed;
        private bool _verifyRevealed;
        private readonly DispatcherTimer _revealTimer;

        // Flow flags
        private bool _settingPwProgrammatically;   // suppresses verify/meter when we SetPassword(...)
        private bool _verifyRowShown;              // tracks whether we've made the verify row visible due to user input

        public CategoryitemEditNew()
        {
            InitializeComponent();

            Loaded += CategoryitemEditNew_Loaded;
            Unloaded += CategoryitemEditNew_Unloaded;

            // Detect paste/typing on main password box (helps catch Ctrl+V etc.)
            pwdPassword.PreviewKeyDown += PwdPassword_PreviewKeyDown;

            _revealTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            _revealTimer.Tick += (_, __) =>
            {
                HideMainPassword();
                HideVerifyPassword();
            };
        }

        /* ======================= Lifecycle ======================= */

        private void CategoryitemEditNew_Loaded(object? sender, RoutedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[ITEM-EDIT] Loaded");
#endif
            ClearForm();
            HideMainPassword();
            HideVerifyPassword();
            HideStrengthRow();
            HideVerifyRow();
        }

        private void CategoryitemEditNew_Unloaded(object? sender, RoutedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[ITEM-EDIT] Unloaded");
#endif
            _revealTimer.Stop();
            WipeSensitiveFields();
            HideStrengthRow();
            HideVerifyRow();
        }

        /* ======================= Configuration ======================= */

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

        /* ======================= Buttons ======================= */

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
                Debug.WriteLine($"[ITEM-EDIT] Password validation failed: {error}");
                PasswordValidationFailed?.Invoke(this, error);
                if (VerifyRow.Visibility == Visibility.Visible && !string.IsNullOrEmpty(pwdVerify.Password))
                    pwdVerify.Focus();
                else
                    pwdPassword.Focus();
                return;
            }

            try
            {
                // TODO: persist (insert/update)
#if DEBUG
                Debug.WriteLine($"[ITEM-EDIT] Saving item for categoryKey={_categoryKey}");
#endif
                Submitted?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine($"[ITEM-EDIT][ERR] {ex}");
#endif
                MessageBox.Show("Error saving item.\n\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnCancel_Click(object? sender, RoutedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[ITEM-EDIT] Cancel clicked");
#endif
            _revealTimer.Stop();
            HideMainPassword();
            HideVerifyPassword();
            WipeSensitiveFields();
            HideStrengthRow();
            HideVerifyRow();
            Canceled?.Invoke(this, EventArgs.Empty);
        }

        private void BtnGeneratePassword_Click(object? sender, RoutedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[ITEM-EDIT] GeneratePassword clicked");
#endif
            try
            {
                var generated = SecurePassword.GenerateAsString(12); // sensible default
                _settingPwProgrammatically = true;
                try { SetPassword(generated); }
                finally { _settingPwProgrammatically = false; }

                // Generated passwords: keep both the meter and verify row hidden
                HideStrengthRow();
                HideVerifyRow();

                // Optionally: ShowMainPassword();
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine($"[ITEM-EDIT][ERR][GeneratePassword] {ex}");
#endif
                PasswordValidationFailed?.Invoke(this, "Password generator failed.");
            }
        }

        private void BtnTogglePasswordReveal_Click(object? sender, RoutedEventArgs e)
        {
            if (_mainRevealed) HideMainPassword();
            else ShowMainPassword();
        }

        private void BtnToggleVerifyReveal_Click(object? sender, RoutedEventArgs e)
        {
            if (_verifyRevealed) HideVerifyPassword();
            else ShowVerifyPassword();
        }

        /* ======================= Password Helpers ======================= */

        private void PwdPassword_PreviewKeyDown(object? sender, KeyEventArgs e)
        {
            // If user pastes, we’ll get PasswordChanged right after this; just ensure we treat it as manual.
            if (_settingPwProgrammatically) return;

            bool pasteCombo =
                (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) ||
                (e.Key == Key.Insert && (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift);

            if (pasteCombo) _verifyRowShown = false; // force re-check on change
        }

        private void pwdPassword_PasswordChanged(object? sender, RoutedEventArgs e)
        {
            if (_revealTimer.IsEnabled) { _revealTimer.Stop(); _revealTimer.Start(); }

            if (_mainRevealed)
                txtPasswordPlain.Text = pwdPassword.Password; // keep visible copy in sync

            if (_settingPwProgrammatically)
                return;

            // Manual typing or paste: show verify row as soon as we have any content
            if (!string.IsNullOrEmpty(pwdPassword.Password))
            {
                EnsureVerifyRowVisibleForManual();
            }
            else
            {
                // If user cleared the main password, reset verify row too
                HideVerifyRow();
            }

            // Strength: only while failing policy
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

            if (_verifyRevealed)
                txtVerifyPlain.Text = pwdVerify.Password;
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

            _revealTimer.Stop(); _revealTimer.Start();
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
            if (_mainRevealed) txtPasswordPlain.Text = value;

            // When programmatic, do NOT show verify row
            HideVerifyRow();
        }

        /// <summary>
        /// If password is present, validate it (>=8 chars and >=3 of 4 classes).
        /// If verify row is visible, also require an exact match.
        /// </summary>
        private bool ValidatePasswordForSubmission(out string error)
        {
            error = string.Empty;
            var pw = pwdPassword.Password ?? string.Empty;

            // Empty password allowed by this control; host can decide otherwise.
            if (string.IsNullOrEmpty(pw))
                return true;

            if (VerifyRow.Visibility == Visibility.Visible)
            {
                var verify = pwdVerify.Password ?? string.Empty;
                if (!string.Equals(pw, verify, StringComparison.Ordinal))
                {
                    error = "Passwords do not match.";
                    return false;
                }
            }

            if (!SecurePassword.IsPasswordValid(pw, pw, out var pwError))
            {
                error = pwError; // your shared validator’s message
                return false;
            }

            return true;
        }

        /* ======================= Strength Panel (only on failure) ======================= */

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
                HideStrengthRow(); // don’t show hints after it passes
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
            if (TryFindResource(brushKey) is System.Windows.Media.Brush b)
                PwStrengthBar.Foreground = b;

            var tips = new System.Collections.Generic.List<string>();
            if (!lengthOk) tips.Add("Use at least 8 characters.");
            if (!pw.Any(char.IsLower)) tips.Add("Add a lowercase letter.");
            if (!pw.Any(char.IsUpper)) tips.Add("Add an uppercase letter.");
            if (!pw.Any(char.IsDigit)) tips.Add("Add a digit.");
            if (!pw.Any(ch => !char.IsLetterOrDigit(ch))) tips.Add("Add a special character.");
            PwTipsList.ItemsSource = tips;
        }

        /* ======================= Verify Row helpers ======================= */

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
        }

        private void HideVerifyRow()
        {
            VerifyRow.Visibility = Visibility.Collapsed;
            _verifyRowShown = false;
            pwdVerify.Password = string.Empty;
            txtVerifyPlain.Text = string.Empty;
            HideVerifyPassword();
        }

        /* ======================= Field Helpers ======================= */

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
            }
            catch
            {
                // best-effort wipes; ignore failures
            }
        }
    }
}
