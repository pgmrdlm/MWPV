// File: View/UserControls/CategoryItems/CategoryItemBasicPanel.xaml.cs
//
// FULL REWRITE
//
// Change requested (THIS STEP ONLY):
// - Increase PIN max length from 6 to 12.
// - Keep PIN numeric-only.
// - Update validation message text to 4–12 digits.
// - No other behavior changes.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MWPV.Services;
using MWPV.Utilities.Helpers;
using MWPV.Utilities.UI;
using MWPV.Utilities.Signatures;
using Security.Utility.Storage;            // SecureEncryptedDataStore (SEDS) + SecurePassword
using Security.Utility.Validation;
using Security.Utility.Wiping;

namespace MWPV.View.UserControls.CategoryItems
{
    public partial class CategoryItemBasicPanel : UserControl
    {
        public event EventHandler? SaveRequested;
        public event EventHandler? CancelRequested;
        public event EventHandler<string>? PasswordValidationFailed;

        // CI_SecretStorage is NOT NULL in DB
        private const int SecretStorage_Default = 0;

        // Mode detection (SEDS)
        private static readonly TimeSpan ClipboardTtl = TimeSpan.FromSeconds(20);

        private int _activeEntityId;
        private string? _primaryAccountNumberPlain;
        internal string? PrimaryAccountNumberPlain => _primaryAccountNumberPlain;

        // Existing item = CurrentEntityId > 0
        private bool IsExistingItem => _activeEntityId > 0;

        // Existing items start view-only; the Edit pill unlocks editing.
        private bool _editUnlocked;

        // Central truth: "view-only" means existing item AND not unlocked.
        private bool IsViewOnly => IsExistingItem && !_editUnlocked;

        // Prevent event stacking
        private bool _uiEventsHooked;

        // Loading guard (prevents handlers from doing UI side-effects while we populate)
        private bool _isPopulating;

        // Reveal state
        private bool _mainRevealed;
        private bool _verifyRevealed;
        private bool _phoneRevealed;
        private bool _primaryAccountNumberRevealed;
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

        // Duplicate-check guard
        private string _lastNameChecked = string.Empty;

        // PIN rules
        private const int PinMinLen = 4;
        private const int PinMaxLen = 12;

        // Signature state: ORIGINAL vs CURRENT
        // NOTE: PasswordFingerprint ORIGINAL only in this file.
        // NOTE: Non-sensitive baselines ORIGINAL only in this file.
        private readonly CategoryItemSignatureState _sigState = new();

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

            ConfigureModeFromSeds();

            // Populate if we are viewing an existing item
            if (IsExistingItem)
                PopulateFromDbForCurrentEntity();

            // Apply initial protection (view-only for existing items unless unlocked)
            ApplyModeProtection();
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

            _sigState.Clear();
        }

        /* ======================= Mode detection helper ======================= */

        private void ConfigureModeFromSeds()
        {
            _activeEntityId = 0;
            _editUnlocked = false; // existing items start locked each time we load/reload
            _lastNameChecked = string.Empty;

            // IMPORTANT: always clear signature state when mode changes
            _sigState.Clear();

            try
            {
                _activeEntityId = CategoryItemSedsContextHelper.TryGetCurrentCategoryItemId() ?? 0;
            }
            finally
            {
#if DEBUG
                Debug.WriteLine($"[BASIC][MODE] CurrentEntityId={_activeEntityId} => {(IsExistingItem ? "EXISTING (VIEW-ONLY)" : "ADD (EDITABLE)")}");
#endif
            }
        }

        private void ApplyModeProtection()
        {
            bool viewOnly = IsViewOnly;

            // Edit pill: only visible while view-only
            btnEditPill.Visibility = viewOnly ? Visibility.Visible : Visibility.Collapsed;

            // Save must not happen in view-only
            btnSubmit.IsEnabled = !viewOnly;
            UpdateSubmitButtonCaption();

            // Cancel always allowed
            btnCancel.IsEnabled = true;

            // TextBoxes: read-only keeps content readable/selectable
            txtItemName.IsReadOnly = viewOnly;
            txtUsername.IsReadOnly = viewOnly;
            txtUrl.IsReadOnly = viewOnly;
            txtDescription.IsReadOnly = viewOnly;

            // PasswordBoxes cannot be read-only, so disable edit
            pwdPassword.IsEnabled = !viewOnly;
            pwdVerify.IsEnabled = !viewOnly;
            pwdPin.IsEnabled = !viewOnly;
            pwdEmail.IsEnabled = !viewOnly;
            txtPhone.IsEnabled = !viewOnly; // PasswordBox named txtPhone

            // Editing actions should be disabled
            chkBookmarkOnly.IsEnabled = !viewOnly;
            chkIsActive.IsEnabled = !viewOnly;

            // Replace Generate with Copy in view-only
            btnGeneratePassword.Visibility = viewOnly ? Visibility.Collapsed : Visibility.Visible;
            btnCopyPassword.Visibility = viewOnly ? Visibility.Visible : Visibility.Collapsed;
            btnGeneratePassword.IsEnabled = !viewOnly;

            // View-only copy buttons
            btnCopyPhone.Visibility = viewOnly ? Visibility.Visible : Visibility.Collapsed;
            btnCopyEmail.Visibility = viewOnly ? Visibility.Visible : Visibility.Collapsed;
            btnCopyPin.Visibility = viewOnly ? Visibility.Visible : Visibility.Collapsed;

            // Username / URL copy buttons (view-only only)
            btnCopyUsername.Visibility = viewOnly ? Visibility.Visible : Visibility.Collapsed;
            btnCopyUrl.Visibility = viewOnly ? Visibility.Visible : Visibility.Collapsed;

            // Reveal actions are allowed even in view-only
            btnTogglePasswordReveal.IsEnabled = true;
            btnToggleVerifyReveal.IsEnabled = true;
            btnTogglePinReveal.IsEnabled = true;
            btnTogglePhoneReveal.IsEnabled = true;
            btnToggleEmailReveal.IsEnabled = true;

            UpdateCopyButtonStates();

#if DEBUG
            Debug.WriteLine($"[BASIC][MODE] Applied: IsExisting={IsExistingItem} EditUnlocked={_editUnlocked} => ViewOnly={viewOnly}");
#endif
        }

        private void UpdateSubmitButtonCaption()
        {
            if (txtSubmitCaption == null)
                return;

            txtSubmitCaption.Text = IsExistingItem ? "Save" : "Add";
        }

        private void UpdateCopyButtonStates()
        {
            bool hasPrimaryAccountNumber = !string.IsNullOrEmpty(pwdPrimaryAccountNumber.Password);
            btnCopyPrimaryAccountNumber.IsEnabled = hasPrimaryAccountNumber;
            btnTogglePrimaryAccountNumberReveal.IsEnabled = hasPrimaryAccountNumber;

            if (!IsViewOnly)
            {
                btnCopyPassword.IsEnabled = false;
                btnCopyPhone.IsEnabled = false;
                btnCopyEmail.IsEnabled = false;
                btnCopyPin.IsEnabled = false;
                btnCopyUsername.IsEnabled = false;
                btnCopyUrl.IsEnabled = false;
                return;
            }

            bool isBookmarkOnly = chkBookmarkOnly.IsChecked == true;

            bool hasPassword = !string.IsNullOrEmpty(pwdPassword.Password);
            btnCopyPassword.IsEnabled = hasPassword && !isBookmarkOnly;

            bool hasPhone = !string.IsNullOrEmpty(txtPhone.Password);
            bool hasEmail = !string.IsNullOrEmpty(pwdEmail.Password);
            bool hasPin = !string.IsNullOrEmpty(pwdPin.Password);

            btnCopyPhone.IsEnabled = hasPhone;
            btnCopyEmail.IsEnabled = hasEmail;
            btnCopyPin.IsEnabled = hasPin;

            bool hasUser = !string.IsNullOrWhiteSpace(txtUsername.Text);
            btnCopyUsername.IsEnabled = hasUser;

            bool hasUrl = !string.IsNullOrWhiteSpace(txtUrl.Text);
            btnCopyUrl.IsEnabled = hasUrl;
        }

        /* ======================= View-only -> Edit unlock ======================= */

        private void btnEditPill_Click(object? sender, RoutedEventArgs e)
        {
            if (!IsExistingItem) return;
            if (!IsViewOnly) return;

#if DEBUG
            Debug.WriteLine("[BASIC][EDIT] Edit pill clicked -> unlocking edit mode");
#endif
            _editUnlocked = true;

            UpdateCopyButtonStates();
            ApplyModeProtection();

            // When we unlock editing, we re-validate name next time it loses focus or on submit
            _lastNameChecked = string.Empty;
        }

        /* ======================= Populate ======================= */

        public void PopulateFromDbForCurrentEntity()
        {
            ConfigureModeFromSeds();

            if (!IsExistingItem)
            {
#if DEBUG
                Debug.WriteLine("[BASIC][POP] Not existing item -> skipping DB populate");
#endif
                ApplyModeProtection();
                return;
            }

            LoadAndApplyByItemId(_activeEntityId);

            ApplyModeProtection();
        }

        private void LoadAndApplyByItemId(long itemId)
        {
            if (itemId <= 0) return;

#if DEBUG
            Debug.WriteLine($"[BASIC][POP] Loading itemId={itemId}");
#endif

            _isPopulating = true;
            try
            {
                ClearForm();
                ResetUiState();

                // always start clean, then capture ORIGINAL after UI has DB values
                _sigState.Clear();

                var row = CategoryItemService.LoadCategoryItemBasicById(itemId);
                if (row == null)
                {
#if DEBUG
                    Debug.WriteLine($"[BASIC][POP] DB returned null for itemId={itemId}");
#endif
                    return;
                }

                _primaryAccountNumberPlain =
                    tmp_CategoryItemAccountsService.LoadPrimaryAccountNumberPlainByItemId(itemId);

                string? pwPlain = null;
                if (row.BookMarkOnly == 0)
                {
                    pwPlain = CategoryItemService.LoadMostRecentPasswordPlainByItemId(itemId);
#if DEBUG
                    Debug.WriteLine($"[BASIC][POP] MostRecentPw: {(pwPlain == null ? "NULL" : $"LEN={pwPlain.Length}")}");
#endif
                }

                ApplyBasicRowToUi(row, pwPlain);

                // =========================================================
                // NON-SENSITIVE: ORIGINAL "before" baselines from loaded row.
                // No current/after compare here.
                // =========================================================
                CaptureOriginalNonSensitiveBeforeFromRow(row, tag: "LOAD/DB->BEFORE");

                // =========================================================
                // PASSWORD ONLY: ORIGINAL fingerprint is loaded from DB sig.
                // NO calculating here.
                // =========================================================
                CaptureOriginalPasswordFingerprintFromDb(itemId, tag: "LOAD/DB->BEFORE");
            }
            finally
            {
                _isPopulating = false;
            }
        }

        private void ApplyBasicRowToUi(CategoryItemService.CategoryItemBasicRow row, string? mostRecentPasswordPlain)
        {
            txtItemName.Text = row.Name ?? string.Empty;
            txtUsername.Text = row.Username ?? string.Empty;
            txtUrl.Text = row.SignInUrl ?? string.Empty;
            txtDescription.Text = row.Description ?? string.Empty;

            chkBookmarkOnly.IsChecked = row.BookMarkOnly == 1;
            chkIsActive.IsChecked = !row.IsActive.HasValue || row.IsActive.Value == 1;

            if (!string.IsNullOrEmpty(row.AccountEmailPlain))
            {
                _lastEmailChecked = row.AccountEmailPlain.Trim();
                pwdEmail.Password = row.AccountEmailPlain;
                ClearEmailValidation();
            }
            else
            {
                _lastEmailChecked = string.Empty;
                UICleaner.Clear(pwdEmail);
                ClearEmailValidation();
            }

            if (!string.IsNullOrEmpty(row.AccountPhonePlain))
            {
                txtPhone.Password = row.AccountPhonePlain;
                ClearPhoneError();
            }
            else
            {
                UICleaner.Clear(txtPhone);
                ClearPhoneError();
            }

            if (!string.IsNullOrEmpty(PrimaryAccountNumberPlain))
            {
                pwdPrimaryAccountNumber.Password = PrimaryAccountNumberPlain;
            }
            else
            {
                UICleaner.Clear(pwdPrimaryAccountNumber);
            }

            if (!string.IsNullOrEmpty(row.PinPlain))
            {
                pwdPin.Password = row.PinPlain;
                ClearPinError();
            }
            else
            {
                UICleaner.Clear(pwdPin);
                ClearPinError();
            }

            _settingPwProgrammatically = true;
            try
            {
                if (row.BookMarkOnly == 1)
                {
                    UICleaner.Clear(pwdPassword);
                    HideVerifyRow();
                    HideVerifyError();
                    HideStrengthRow();
                }
                else
                {
                    if (mostRecentPasswordPlain != null)
                    {
                        pwdPassword.Password = mostRecentPasswordPlain;
                        HideVerifyRow();
                        HideVerifyError();
                        HideStrengthRow();
                    }
                    else
                    {
                        UICleaner.Clear(pwdPassword);
                        HideVerifyRow();
                        HideVerifyError();
                        HideStrengthRow();
                    }
                }
            }
            finally
            {
                _settingPwProgrammatically = false;
            }

            HideAllRevealsAndStopTimer();
            ClearPlainRevealOverlays();

            ClearItemNameError();

            // Reset duplicate-check cache to match loaded name
            _lastNameChecked = (txtItemName.Text ?? string.Empty).Trim();

            UpdateCopyButtonStates();
        }

        /* ======================= NON-SENSITIVE BEFORE BASELINES (DB -> BEFORE) ======================= */

        private void CaptureOriginalNonSensitiveBeforeFromRow(CategoryItemService.CategoryItemBasicRow row, string tag)
        {
            if (row == null)
                return;

            try
            {
                _sigState.Name.SetOriginal(row.Name);
                _sigState.UserName.SetOriginal(row.Username);
                _sigState.SignInUrl.SetOriginal(row.SignInUrl);

                _sigState.Description.SetOriginal(row.Description);
                _sigState.Notes.SetOriginal(row.Description);

                _sigState.BookMarkOnly.SetOriginal(row.BookMarkOnly == 1);

#if DEBUG
                Debug.WriteLine($"[BASIC][BEFORE-NON-SIG][{tag}] Loaded originals: " +
                                $"NameLen={(row.Name?.Length ?? 0)} UserLen={(row.Username?.Length ?? 0)} UrlLen={(row.SignInUrl?.Length ?? 0)} " +
                                $"DescLen={(row.Description?.Length ?? 0)} BookmarkOnly={row.BookMarkOnly}");
#endif
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine($"[BASIC][BEFORE-NON-SIG][{tag}] FAILED: {ex.GetType().Name} {ex.Message}");
#endif
            }
        }

        /* ======================= Host-facing API ======================= */

        public void ClearForm()
        {
            WipeSensitiveFields();

            txtItemName.Text = string.Empty;
            txtUsername.Text = string.Empty;
            txtUrl.Text = string.Empty;
            txtDescription.Text = string.Empty;

            chkIsActive.IsChecked = true;

            _lastEmailChecked = string.Empty;
            _lastNameChecked = string.Empty;
            _primaryAccountNumberPlain = null;

            ClearItemNameError();
            ClearEmailValidation();
            ClearPhoneError();
            ClearPinError();

            _sigState.Clear();
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
            UICleaner.Clear(pwdPrimaryAccountNumber);
            UICleaner.Clear(pwdPin);
            UICleaner.Clear(pwdEmail);

            ClearPlainRevealOverlays();

            HideStrengthRow();
            HideVerifyError();

            UpdateCopyButtonStates();

            // wiping UI wipes signature state too
            _sigState.Clear();
        }

        public void WipeAllForHostClose()
        {
#if DEBUG
            Debug.WriteLine("[BASIC] WipeAllForHostClose");
#endif
            ResetUiState();
            WipeSensitiveFields();
        }

        /// <summary>
        /// Host-callable "press Cancel" that reuses the existing cancel path.
        /// No new wipe logic is introduced here.
        /// </summary>
        public void ForceCancelFromHost()
        {
#if DEBUG
            Debug.WriteLine("[BASIC] ForceCancelFromHost -> raising CancelRequested");
#endif
            try
            {
                CancelRequested?.Invoke(this, EventArgs.Empty);
            }
            catch
            {
                // swallow (host-controlled flow)
            }
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

        public bool TryValidateAllForSubmit(
            out bool isBookmarkOnly,
            out bool okName,
            out bool okPassword,
            out bool okPin,
            out bool okEmail,
            out bool okPhone)
        {
            isBookmarkOnly = chkBookmarkOnly.IsChecked == true;

            okName = ValidateItemName(forSubmit: true);
            okPassword = ValidatePasswordForSubmission(isBookmarkOnly, out _);
            okPin = ValidatePin(forSubmit: true);
            okEmail = ValidateEmailForSubmit();
            okPhone = ValidatePhoneNumber(forSubmit: true);

            return okName && okPassword && okPin && okEmail && okPhone;
        }

        public void FocusFirstError(bool okName, bool okPassword, bool okPin, bool okEmail, bool okPhone, bool isBookmarkOnly)
        {
            if (!okName) { txtItemName.Focus(); return; }

            if (!okPassword)
            {
                if (!isBookmarkOnly && VerifyRow.Visibility == Visibility.Visible)
                {
                    var verify = pwdVerify.Password ?? string.Empty;
                    if (!string.IsNullOrEmpty(verify)) pwdVerify.Focus();
                    else pwdPassword.Focus();
                }
                else
                {
                    pwdPassword.Focus();
                }
                return;
            }

            if (!okPin) { pwdPin.Focus(); return; }
            if (!okEmail) { pwdEmail.Focus(); return; }
            if (!okPhone) { txtPhone.Focus(); return; }
        }

        /* ======================= Safe non-null SecretStorage ======================= */

        public int GetSecretStorageOrDefault() => GetSecretStorageOrDefault(chkBookmarkOnly.IsChecked == true);

        public int GetSecretStorageOrDefault(bool isBookmarkOnly)
        {
            _ = isBookmarkOnly;
            return SecretStorage_Default;
        }

        /* ======================= Value getters for Service Insert ======================= */

        public string GetItemNameTrim() => (txtItemName.Text ?? string.Empty).Trim();

        public string? GetDescriptionTrimOrNull()
        {
            var s = (txtDescription.Text ?? string.Empty).Trim();
            return s.Length == 0 ? null : s;
        }

        public string? GetUsernameTrimOrNull()
        {
            var s = (txtUsername.Text ?? string.Empty).Trim();
            return s.Length == 0 ? null : s;
        }

        public string? GetUrlTrimOrNull()
        {
            var s = (txtUrl.Text ?? string.Empty).Trim();
            return s.Length == 0 ? null : s;
        }

        public string? GetEmailTrimOrNull()
        {
            var s = (pwdEmail.Password ?? string.Empty).Trim();
            return s.Length == 0 ? null : s;
        }

        public string? GetPhoneTrimOrNull()
        {
            var s = (txtPhone.Password ?? string.Empty).Trim();
            return s.Length == 0 ? null : s;
        }

        public string? GetPinTrimOrNull()
        {
            var s = (pwdPin.Password ?? string.Empty).Trim();
            return s.Length == 0 ? null : s;
        }

        public int GetIsActiveInt() => chkIsActive.IsChecked == true ? 1 : 0;

        public string? GetPasswordPlainOrNull()
        {
            var pw = pwdPassword.Password;
            return string.IsNullOrEmpty(pw) ? null : pw;
        }

        public string? GetPasswordTrimOrNull() => GetPasswordPlainOrNull();

        /* ======================= PASSWORD SIGNATURE: ORIGINAL ONLY (DB -> BEFORE) ======================= */

        public byte[]? GetOriginalPasswordFingerprintCopy()
        {
            try
            {
                var orig = _sigState.PasswordFingerprint.OriginalSig;
                if (orig == null || orig.Length == 0)
                    return null;

                var copy = new byte[orig.Length];
                Buffer.BlockCopy(orig, 0, copy, 0, orig.Length);
                return copy;
            }
            catch
            {
                return null;
            }
        }

        private void CaptureOriginalPasswordFingerprintFromDb(long itemId, string tag)
        {
            if (itemId <= 0)
            {
                _sigState.PasswordFingerprint.SetOriginal(null);
#if DEBUG
                Debug.WriteLine($"[SIGPW][{tag}] Skipped (invalid itemId)");
#endif
                return;
            }

            if (chkBookmarkOnly.IsChecked == true)
            {
                _sigState.PasswordFingerprint.SetOriginal(null);
#if DEBUG
                Debug.WriteLine($"[SIGPW][{tag}] BookmarkOnly=1 => ORIGINAL=<null>");
#endif
                return;
            }

            byte[]? dbSig = null;
            try
            {
                dbSig = CategoryItemService.LoadMostRecentPasswordHistoryByItemId(itemId)?.PwFp;

                _sigState.PasswordFingerprint.SetOriginal(dbSig);

#if DEBUG
                Debug.WriteLine($"[SIGPW][{tag}] itemId={itemId} ORIGINAL={(dbSig == null ? "<null>" : Convert.ToHexString(dbSig))}");
#endif
            }
            catch (Exception ex)
            {
                _sigState.PasswordFingerprint.SetOriginal(null);
#if DEBUG
                Debug.WriteLine($"[SIGPW][{tag}] FAILED to load DB signature: {ex.GetType().Name} {ex.Message}");
#endif
            }
            finally
            {
                // dbSig is now held by _sigState; do NOT wipe here.
            }
        }

        /* ======================= Save / Cancel ======================= */

        private void btnSubmit_Click(object? sender, RoutedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[BASIC] Save clicked");
#endif
            if (IsViewOnly)
            {
#if DEBUG
                Debug.WriteLine("[BASIC] Save suppressed: view-only mode");
#endif
                return;
            }

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
            Debug.WriteLine("[BASIC] Reveal timer elapsed, hiding all reveals");
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
            HidePrimaryAccountNumber();
            HidePin();
            HideEmail();
        }

        private void TouchRevealTimerIfNeeded()
        {
            bool anyRevealed = _mainRevealed || _verifyRevealed || _phoneRevealed || _primaryAccountNumberRevealed || _pinRevealed || _emailRevealed;
            _revealAutoHide.Touch(anyRevealed);
        }

        /* ======================= Item name validation (required + global-duplicate) ======================= */

        private bool ValidateItemName(bool forSubmit)
        {
            var name = (txtItemName.Text ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                ShowItemNameError("Item name is required.");
                return false;
            }

            // Never do duplicate checks while populating or in view-only.
            if (_isPopulating || IsViewOnly)
            {
                ClearItemNameError();
                return true;
            }

            if (!forSubmit && string.Equals(name, _lastNameChecked, StringComparison.Ordinal))
            {
                ClearItemNameError();
                return true;
            }

            _lastNameChecked = name;

            try
            {
                long? excludeId = (IsExistingItem && _editUnlocked) ? _activeEntityId : (long?)null;

                bool exists = CategoryItemService.ItemNameExistsAcrossAllCategories(name, excludeItemId: excludeId);

                if (exists)
                {
                    ShowItemNameError("That item name is already used. Please choose a different name.");
                    return false;
                }
            }
            catch
            {
#if DEBUG
                Debug.WriteLine("[BASIC][NAME] Duplicate-check failed (exception).");
#endif
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
            if (_itemNameDefaultBackground != null) txtItemName.Background = _itemNameDefaultBackground;
            if (_itemNameDefaultBorderBrush != null) txtItemName.BorderBrush = _itemNameDefaultBorderBrush;
        }

        /* ======================= Password / Verify ======================= */

        private void BtnGeneratePassword_Click(object? sender, RoutedEventArgs e)
        {
            if (IsViewOnly) return;

            try
            {
                // CHANGED: use COMPATIBLE generator (picky-site friendly)
                var generated = SecurePassword.GenerateCompatibleAsString(12);

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

        private void BtnCopyPassword_Click(object? sender, RoutedEventArgs e)
        {
            if (!IsViewOnly) return;
            if (chkBookmarkOnly.IsChecked == true) return;

            var pw = pwdPassword.Password;
            if (string.IsNullOrEmpty(pw)) return;

            _ = ClipboardHelper.TryCopySensitiveText(pw, out _, ClipboardTtl, tag: "BASIC.PW");
        }

        private void BtnCopyUsername_Click(object? sender, RoutedEventArgs e)
        {
            if (!IsViewOnly) return;

            var user = (txtUsername.Text ?? string.Empty).Trim();
            if (user.Length == 0) return;

            _ = ClipboardHelper.TryCopySensitiveText(user, out _, ClipboardTtl, tag: "BASIC.USER");
        }

        private void BtnCopyUrl_Click(object? sender, RoutedEventArgs e)
        {
            if (!IsViewOnly) return;

            var url = (txtUrl.Text ?? string.Empty).Trim();
            if (url.Length == 0) return;

            _ = ClipboardHelper.TryCopySensitiveText(url, out _, ClipboardTtl, tag: "BASIC.URL");
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
            if (_isPopulating) return;
            if (IsViewOnly) { e.Handled = true; return; }

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
            if (_isPopulating) return;

            if (_mainRevealed)
                txtPasswordPlain.Text = pwdPassword.Password;

            TouchRevealTimerIfNeeded();

            UpdateCopyButtonStates();

            if (IsViewOnly) return;

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
            if (_isPopulating) return;
            if (_settingPwProgrammatically) return;
            if (IsViewOnly) return;
            if (chkBookmarkOnly.IsChecked == true) return;

            if (!string.IsNullOrEmpty(pwdPassword.Password))
                UpdateStrengthPanelForPolicy();
        }

        private void pwdVerify_PasswordChanged(object? sender, RoutedEventArgs e)
        {
            if (_isPopulating) return;
            if (IsViewOnly) return;

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
            MaskedRevealOverlayHelper.ShowPlainOverlay(pwdPassword, txtPasswordPlain, pwdPassword.Password);
            _mainRevealed = true;
        }

        private void HideMainPassword()
        {
            UICleaner.Clear(txtPasswordPlain);
            MaskedRevealOverlayHelper.RestoreMaskedOverlay(pwdPassword, txtPasswordPlain);
            _mainRevealed = false;
        }

        private void ShowVerifyPassword()
        {
            MaskedRevealOverlayHelper.ShowPlainOverlay(pwdVerify, txtVerifyPlain, pwdVerify.Password);
            _verifyRevealed = true;
        }

        private void HideVerifyPassword()
        {
            UICleaner.Clear(txtVerifyPlain);
            MaskedRevealOverlayHelper.RestoreMaskedOverlay(pwdVerify, txtVerifyPlain);
            _verifyRevealed = false;
        }

        private void SetPassword(string value)
        {
            pwdPassword.Password = value;

            if (_mainRevealed)
                txtPasswordPlain.Text = value;

            HideVerifyRow();
            HideVerifyError();

            UpdateCopyButtonStates();
        }

        private bool ValidatePasswordForSubmission(bool isBookmarkOnly, out string error)
        {
            error = string.Empty;

            if (IsViewOnly)
            {
                error = "View-only mode.";
                return false;
            }

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
            if (_isPopulating) { e.Handled = true; return; }
            if (IsViewOnly) { e.Handled = true; return; }

            if (string.IsNullOrEmpty(e.Text)) { e.Handled = true; return; }

            foreach (char c in e.Text)
            {
                if (!char.IsDigit(c)) { e.Handled = true; return; }
            }

            if (sender is PasswordBox pb)
            {
                if ((pb.Password?.Length ?? 0) + e.Text.Length > PinMaxLen)
                {
                    e.Handled = true;
                    return;
                }
            }

            e.Handled = false;
        }

        private void Pin_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_isPopulating) { e.Handled = true; return; }
            if (IsViewOnly) { e.Handled = true; return; }

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
            if (_isPopulating) { e.CancelCommand(); return; }
            if (IsViewOnly) { e.CancelCommand(); return; }

            if (!e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText, true))
            {
                e.CancelCommand();
                return;
            }

            string paste = (e.SourceDataObject.GetData(DataFormats.UnicodeText) as string) ?? "";
            paste = paste.Trim();

            if (paste.Length == 0) { e.CancelCommand(); return; }

            foreach (char c in paste)
            {
                if (!char.IsDigit(c)) { e.CancelCommand(); return; }
            }

            if (sender is not PasswordBox pb) { e.CancelCommand(); return; }

            int remaining = PinMaxLen - (pb.Password?.Length ?? 0);
            if (remaining <= 0) { e.CancelCommand(); return; }

            if (paste.Length > remaining)
            {
                paste = paste.Substring(0, remaining);
                e.DataObject = new DataObject(DataFormats.UnicodeText, paste);
            }
        }

        private void Pin_PasswordChanged(object? sender, RoutedEventArgs e)
        {
            if (_isPopulating) return;

            if (_pinRevealed)
                txtPinPlain.Text = pwdPin.Password;

            TouchRevealTimerIfNeeded();

            UpdateCopyButtonStates();

            if (IsViewOnly) return;

            if (!string.IsNullOrEmpty(pwdPin.Password) && pwdPin.Password.Length > PinMaxLen)
                pwdPin.Password = pwdPin.Password.Substring(0, PinMaxLen);

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
            if (string.IsNullOrEmpty(pwdPin.Password))
                return;

            if (_pinRevealed) HidePin();
            else ShowPin();

            TouchRevealTimerIfNeeded();
        }

        private void BtnCopyPin_Click(object? sender, RoutedEventArgs e)
        {
            if (!IsViewOnly) return;

            var pin = (pwdPin.Password ?? string.Empty).Trim();
            if (pin.Length == 0) return;

            _ = ClipboardHelper.TryCopySensitiveText(pin, out _, ClipboardTtl, tag: "BASIC.PIN");
        }

        private void ShowPin()
        {
            MaskedRevealOverlayHelper.ShowPlainOverlay(pwdPin, txtPinPlain, pwdPin.Password);
            _pinRevealed = true;
        }

        private void HidePin()
        {
            UICleaner.Clear(txtPinPlain);
            MaskedRevealOverlayHelper.RestoreMaskedOverlay(pwdPin, txtPinPlain);
            _pinRevealed = false;
        }

        private bool ValidatePin(bool forSubmit)
        {
            _ = forSubmit;

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
                ShowPinError("PIN must be 4–12 digits.");
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
            if (_pinDefaultBackground != null) pwdPin.Background = _pinDefaultBackground;
            if (_pinDefaultBorderBrush != null) pwdPin.BorderBrush = _pinDefaultBorderBrush;
        }

        /* ======================= Email ======================= */

        private void pwdEmail_PasswordChanged(object? sender, RoutedEventArgs e)
        {
            if (_isPopulating) return;

            if (_emailRevealed)
                txtEmailPlain.Text = pwdEmail.Password;

            TouchRevealTimerIfNeeded();

            UpdateCopyButtonStates();

            if (IsViewOnly) return;

            if (string.IsNullOrEmpty(pwdEmail.Password))
            {
                _lastEmailChecked = string.Empty;
                ClearEmailValidation();
                HideEmail();
            }
        }

        private void pwdEmail_LostFocus(object? sender, RoutedEventArgs e)
        {
            if (_isPopulating) return;
            if (IsViewOnly) return;

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

        private void BtnCopyEmail_Click(object? sender, RoutedEventArgs e)
        {
            if (!IsViewOnly) return;

            var email = (pwdEmail.Password ?? string.Empty).Trim();
            if (email.Length == 0) return;

            _ = ClipboardHelper.TryCopySensitiveText(email, out _, ClipboardTtl, tag: "BASIC.EMAIL");
        }

        private void BtnToggleEmailReveal_Click(object? sender, RoutedEventArgs e)
        {
            if (_emailRevealed) HideEmail();
            else ShowEmail();

            TouchRevealTimerIfNeeded();
        }

        private void ShowEmail()
        {
            MaskedRevealOverlayHelper.ShowPlainOverlay(pwdEmail, txtEmailPlain, pwdEmail.Password);
            _emailRevealed = true;
        }

        private void HideEmail()
        {
            UICleaner.Clear(txtEmailPlain);
            MaskedRevealOverlayHelper.RestoreMaskedOverlay(pwdEmail, txtEmailPlain);
            _emailRevealed = false;
        }

        private bool ValidateEmailForSubmit()
        {
            if (IsViewOnly) return true;

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
            if (_emailDefaultBackground != null) pwdEmail.Background = _emailDefaultBackground;
            if (_emailDefaultBorderBrush != null) pwdEmail.BorderBrush = _emailDefaultBorderBrush;
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
            if (_emailDefaultBackground != null) pwdEmail.Background = _emailDefaultBackground;
            if (_emailDefaultBorderBrush != null) pwdEmail.BorderBrush = _emailDefaultBorderBrush;
        }

        /* ======================= Phone ======================= */

        private void txtPhone_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isPopulating) return;
            if (IsViewOnly) return;

            ValidatePhoneNumber(forSubmit: false);
        }

        private void txtPhone_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_isPopulating) return;

            if (_phoneRevealed)
                txtPhonePlain.Text = txtPhone.Password;

            TouchRevealTimerIfNeeded();

            UpdateCopyButtonStates();

            if (IsViewOnly) return;

            if (string.IsNullOrEmpty(txtPhone.Password))
                ClearPhoneError();
        }

        private void BtnCopyPhone_Click(object? sender, RoutedEventArgs e)
        {
            if (!IsViewOnly) return;

            var phone = (txtPhone.Password ?? string.Empty).Trim();
            if (phone.Length == 0) return;

            _ = ClipboardHelper.TryCopySensitiveText(phone, out _, ClipboardTtl, tag: "BASIC.PHONE");
        }

        private void BtnTogglePhoneReveal_Click(object? sender, RoutedEventArgs e)
        {
            if (_phoneRevealed) HidePhone();
            else ShowPhone();

            TouchRevealTimerIfNeeded();
        }

        private void BtnCopyPrimaryAccountNumber_Click(object? sender, RoutedEventArgs e)
        {
            var primaryAccountNumber = (pwdPrimaryAccountNumber.Password ?? string.Empty).Trim();
            if (primaryAccountNumber.Length == 0) return;

            _ = ClipboardHelper.TryCopySensitiveText(primaryAccountNumber, out _, ClipboardTtl, tag: "BASIC.PRIMARY_ACCOUNT");
        }

        private void BtnTogglePrimaryAccountNumberReveal_Click(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(pwdPrimaryAccountNumber.Password))
                return;

            if (_primaryAccountNumberRevealed) HidePrimaryAccountNumber();
            else ShowPrimaryAccountNumber();

            TouchRevealTimerIfNeeded();
        }

        private void ShowPhone()
        {
            MaskedRevealOverlayHelper.ShowPlainOverlay(txtPhone, txtPhonePlain, txtPhone.Password);
            _phoneRevealed = true;
        }

        private void HidePhone()
        {
            UICleaner.Clear(txtPhonePlain);
            MaskedRevealOverlayHelper.RestoreMaskedOverlay(txtPhone, txtPhonePlain);
            _phoneRevealed = false;
        }

        private void ShowPrimaryAccountNumber()
        {
            MaskedRevealOverlayHelper.ShowPlainOverlay(
                pwdPrimaryAccountNumber,
                txtPrimaryAccountNumberPlain,
                pwdPrimaryAccountNumber.Password);
            _primaryAccountNumberRevealed = true;
        }

        private void HidePrimaryAccountNumber()
        {
            UICleaner.Clear(txtPrimaryAccountNumberPlain);
            MaskedRevealOverlayHelper.RestoreMaskedOverlay(
                pwdPrimaryAccountNumber,
                txtPrimaryAccountNumberPlain);
            _primaryAccountNumberRevealed = false;
        }

        private bool ValidatePhoneNumber(bool forSubmit)
        {
            if (IsViewOnly) return true;

            _ = forSubmit;

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
            if (_phoneDefaultBackground != null) txtPhone.Background = _phoneDefaultBackground;
            if (_phoneDefaultBorderBrush != null) txtPhone.BorderBrush = _phoneDefaultBorderBrush;
        }

        /* ======================= Plain reveal overlays ======================= */

        private void ClearPlainRevealOverlays()
        {
            UICleaner.Clear(txtPasswordPlain);
            UICleaner.Clear(txtVerifyPlain);
            UICleaner.Clear(txtPhonePlain);
            UICleaner.Clear(txtPrimaryAccountNumberPlain);
            UICleaner.Clear(txtPinPlain);
            UICleaner.Clear(txtEmailPlain);
        }

        /* ======================= Default visuals caching ======================= */

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

        /* ======================= UI event hooking ======================= */

        private void HookUiEventsOnce()
        {
            if (_uiEventsHooked)
                return;

            btnEditPill.Click -= btnEditPill_Click;
            btnEditPill.Click += btnEditPill_Click;

            btnSubmit.Click -= btnSubmit_Click;
            btnSubmit.Click += btnSubmit_Click;

            btnCancel.Click -= btnCancel_Click;
            btnCancel.Click += btnCancel_Click;

            txtItemName.TextChanged -= txtItemName_TextChanged;
            txtItemName.TextChanged += txtItemName_TextChanged;

            // NEW: item name LostFocus triggers duplicate check (but never in view-only)
            txtItemName.LostFocus -= txtItemName_LostFocus;
            txtItemName.LostFocus += txtItemName_LostFocus;

            txtUsername.TextChanged -= txtUsername_TextChanged;
            txtUsername.TextChanged += txtUsername_TextChanged;

            txtUrl.TextChanged -= txtUrl_TextChanged;
            txtUrl.TextChanged += txtUrl_TextChanged;

            chkBookmarkOnly.Checked -= chkBookmarkOnly_Changed;
            chkBookmarkOnly.Unchecked -= chkBookmarkOnly_Changed;
            chkBookmarkOnly.Checked += chkBookmarkOnly_Changed;
            chkBookmarkOnly.Unchecked += chkBookmarkOnly_Changed;

            pwdPassword.PreviewKeyDown -= PwdPassword_PreviewKeyDown;
            pwdPassword.PreviewKeyDown += PwdPassword_PreviewKeyDown;

            pwdPassword.PasswordChanged -= pwdPassword_PasswordChanged;
            pwdPassword.PasswordChanged += pwdPassword_PasswordChanged;

            pwdPassword.GotFocus -= pwdPassword_GotFocus;
            pwdPassword.GotFocus += pwdPassword_GotFocus;

            pwdVerify.PasswordChanged -= pwdVerify_PasswordChanged;
            pwdVerify.PasswordChanged += pwdVerify_PasswordChanged;

            btnTogglePasswordReveal.Click -= BtnTogglePasswordReveal_Click;
            btnTogglePasswordReveal.Click += BtnTogglePasswordReveal_Click;

            btnToggleVerifyReveal.Click -= BtnToggleVerifyReveal_Click;
            btnToggleVerifyReveal.Click += BtnToggleVerifyReveal_Click;

            btnGeneratePassword.Click -= BtnGeneratePassword_Click;
            btnGeneratePassword.Click += BtnGeneratePassword_Click;

            btnCopyPassword.Click -= BtnCopyPassword_Click;
            btnCopyPassword.Click += BtnCopyPassword_Click;

            btnCopyUsername.Click -= BtnCopyUsername_Click;
            btnCopyUsername.Click += BtnCopyUsername_Click;

            btnCopyUrl.Click -= BtnCopyUrl_Click;
            btnCopyUrl.Click += BtnCopyUrl_Click;

            pwdPin.PreviewTextInput -= Pin_PreviewTextInput;
            pwdPin.PreviewTextInput += Pin_PreviewTextInput;

            pwdPin.PreviewKeyDown -= Pin_PreviewKeyDown;
            pwdPin.PreviewKeyDown += Pin_PreviewKeyDown;

            pwdPin.PasswordChanged -= Pin_PasswordChanged;
            pwdPin.PasswordChanged += Pin_PasswordChanged;

            DataObject.RemovePastingHandler(pwdPin, Pin_Pasting);
            DataObject.AddPastingHandler(pwdPin, Pin_Pasting);

            btnTogglePinReveal.Click -= Pin_ToggleReveal_Click;
            btnTogglePinReveal.Click += Pin_ToggleReveal_Click;

            btnCopyPin.Click -= BtnCopyPin_Click;
            btnCopyPin.Click += BtnCopyPin_Click;

            pwdEmail.PasswordChanged -= pwdEmail_PasswordChanged;
            pwdEmail.PasswordChanged += pwdEmail_PasswordChanged;

            pwdEmail.LostFocus -= pwdEmail_LostFocus;
            pwdEmail.LostFocus += pwdEmail_LostFocus;

            btnToggleEmailReveal.Click -= BtnToggleEmailReveal_Click;
            btnToggleEmailReveal.Click += BtnToggleEmailReveal_Click;

            btnCopyEmail.Click -= BtnCopyEmail_Click;
            btnCopyEmail.Click += BtnCopyEmail_Click;

            txtPhone.PasswordChanged -= txtPhone_PasswordChanged;
            txtPhone.PasswordChanged += txtPhone_PasswordChanged;

            txtPhone.LostFocus -= txtPhone_LostFocus;
            txtPhone.LostFocus += txtPhone_LostFocus;

            btnTogglePhoneReveal.Click -= BtnTogglePhoneReveal_Click;
            btnTogglePhoneReveal.Click += BtnTogglePhoneReveal_Click;

            btnCopyPhone.Click -= BtnCopyPhone_Click;
            btnCopyPhone.Click += BtnCopyPhone_Click;

            btnTogglePrimaryAccountNumberReveal.Click -= BtnTogglePrimaryAccountNumberReveal_Click;
            btnTogglePrimaryAccountNumberReveal.Click += BtnTogglePrimaryAccountNumberReveal_Click;

            btnCopyPrimaryAccountNumber.Click -= BtnCopyPrimaryAccountNumber_Click;
            btnCopyPrimaryAccountNumber.Click += BtnCopyPrimaryAccountNumber_Click;

            _uiEventsHooked = true;
        }

        private void UnhookUiEvents()
        {
            if (!_uiEventsHooked)
                return;

            btnEditPill.Click -= btnEditPill_Click;

            btnSubmit.Click -= btnSubmit_Click;
            btnCancel.Click -= btnCancel_Click;

            txtItemName.TextChanged -= txtItemName_TextChanged;
            txtItemName.LostFocus -= txtItemName_LostFocus;

            txtUsername.TextChanged -= txtUsername_TextChanged;
            txtUrl.TextChanged -= txtUrl_TextChanged;

            chkBookmarkOnly.Checked -= chkBookmarkOnly_Changed;
            chkBookmarkOnly.Unchecked -= chkBookmarkOnly_Changed;

            pwdPassword.PreviewKeyDown -= PwdPassword_PreviewKeyDown;
            pwdPassword.PasswordChanged -= pwdPassword_PasswordChanged;
            pwdPassword.GotFocus -= pwdPassword_GotFocus;

            pwdVerify.PasswordChanged -= pwdVerify_PasswordChanged;

            btnTogglePasswordReveal.Click -= BtnTogglePasswordReveal_Click;
            btnToggleVerifyReveal.Click -= BtnToggleVerifyReveal_Click;
            btnGeneratePassword.Click -= BtnGeneratePassword_Click;
            btnCopyPassword.Click -= BtnCopyPassword_Click;

            btnCopyUsername.Click -= BtnCopyUsername_Click;
            btnCopyUrl.Click -= BtnCopyUrl_Click;

            pwdPin.PreviewTextInput -= Pin_PreviewTextInput;
            pwdPin.PreviewKeyDown -= Pin_PreviewKeyDown;
            pwdPin.PasswordChanged -= Pin_PasswordChanged;
            DataObject.RemovePastingHandler(pwdPin, Pin_Pasting);
            btnTogglePinReveal.Click -= Pin_ToggleReveal_Click;
            btnCopyPin.Click -= BtnCopyPin_Click;

            pwdEmail.PasswordChanged -= pwdEmail_PasswordChanged;
            pwdEmail.LostFocus -= pwdEmail_LostFocus;
            btnToggleEmailReveal.Click -= BtnToggleEmailReveal_Click;
            btnCopyEmail.Click -= BtnCopyEmail_Click;

            txtPhone.PasswordChanged -= txtPhone_PasswordChanged;
            txtPhone.LostFocus -= txtPhone_LostFocus;
            btnTogglePhoneReveal.Click -= BtnTogglePhoneReveal_Click;
            btnCopyPhone.Click -= BtnCopyPhone_Click;

            btnTogglePrimaryAccountNumberReveal.Click -= BtnTogglePrimaryAccountNumberReveal_Click;
            btnCopyPrimaryAccountNumber.Click -= BtnCopyPrimaryAccountNumber_Click;

            _uiEventsHooked = false;
        }

        private void txtItemName_TextChanged(object? sender, TextChangedEventArgs e)
        {
            // While typing, we clear error only if non-empty.
            // Duplicate check happens on LostFocus or submit.
            if (!string.IsNullOrWhiteSpace(txtItemName.Text))
            {
                if (ItemNameErrorPanel.Visibility == Visibility.Visible)
                    ClearItemNameError();
            }

            _lastNameChecked = string.Empty;
        }

        private void txtItemName_LostFocus(object? sender, RoutedEventArgs e)
        {
            if (_isPopulating) return;
            if (IsViewOnly) return;

            _ = ValidateItemName(forSubmit: false);
        }

        private void txtUsername_TextChanged(object? sender, TextChangedEventArgs e)
        {
            if (IsViewOnly)
                UpdateCopyButtonStates();
        }

        private void txtUrl_TextChanged(object? sender, TextChangedEventArgs e)
        {
            if (IsViewOnly)
                UpdateCopyButtonStates();
        }

        private void chkBookmarkOnly_Changed(object? sender, RoutedEventArgs e)
        {
            if (IsViewOnly)
            {
                UpdateCopyButtonStates();
                return;
            }

            if (chkBookmarkOnly.IsChecked == true)
            {
                HideStrengthRow();
                HideVerifyRow();
                HideVerifyError();
            }

            UpdateCopyButtonStates();
        }
    }
}
