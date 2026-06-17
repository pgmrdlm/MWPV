// File: Utilities/Security/AppEntryWindow.xaml.cs
// FULL REWRITE
//
// Hotfix: UpperCaseNotify
//
// Purpose:
// - Adds Caps Lock ON notifications for PASSWORD typing only, on these two PasswordBox controls:
//     pbPassword, pbVerifyPassword
// - Uses inline TextBlocks (tbCapsWarnPassword, tbCapsWarnVerify) defined in XAML.
// - No app-wide wiring; window-local only.
// - Preserves existing security/memory-hygiene patterns and existing behavior.
//
// XAML dependency (already added in your XAML rewrite):
// - PasswordBoxes wired to these handlers:
//     PasswordBox_GotKeyboardFocus
//     PasswordBox_LostKeyboardFocus
//     PasswordBox_PreviewKeyDown
// - Inline warning TextBlocks:
//     tbCapsWarnPassword
//     tbCapsWarnVerify

using System;
using System.IO;
using System.Runtime.InteropServices; // SecureString marshal
using System.Security;                // SecureString
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;           // Brush for meter coloring

using Microsoft.Data.Sqlite;          // ok if not used directly here

using MWPV.Services.AppLifecycle;
using MWPV.Services.Upgrade;
using Utilities.Helpers;              // DatabaseHelper (keep), no popup usage
using Utilities.Sql;                  // SqlCagegory, SchemaBootstrap
using Security.Utility;               // SensitiveDataCleaner, SecureEncryptedDataStore, SecurePassword, ServiceSetUp, KeyArchiveVerifier
using Utilities.Diagnostics;          // EarlyLoginFailures, EarlyFailType
using EDT = Utilities.Diagnostics.EarlyFailType;
using Security.Utility.Crypto;        // KeyArchiveVerifier, KeyProvisioner
using MWPV.Utilities.UI;              // UICleaner (UI-only scrubbers)
using MWPV.Utilities.Security;        // PasswordStrengthEvaluator, PasswordStrength
using MWPV.Services;                  // LogCatalogService
using KeyFileLogic;                   // SQLite key-file storage

namespace Utilities.Security
{
    /// <summary>
    /// Setup/Login dialog:
    ///   • First run: create DB, create encrypted key archive, stash DB password + keyset in archive.
    ///   • Normal login: verify existing encrypted archive + password using sentinels.
    ///   • Load SQL catalog and ensure Logs schema exists. (DEBUG) optional smoke test.
    /// </summary>
    public partial class AppEntryWindow : Window
    {
        // SecureEncryptedDataStore keys (file/key names in the archive)
        private const string Key_DBPassword = "DB_Password.txt"; // file name inside the archive
        private const string Key_KeyFile = "KeyFile";            // non-sensitive path
        private const string Key_KeyPW = "KeyPW";                // sensitive password

        // Full path to local encrypted database
        private readonly string _localDbPath = Path.Combine(
            AppPaths.LocalAppDataRoot(),
            "MWPV",
            "MWPV.db"
        );

        // ===== CapsLock warning state =====
        // Avoid thrashing UI updates while typing; track per PasswordBox focus session.
        private bool _capsWarnShownForPassword;
        private bool _capsWarnShownForVerify;

        public AppEntryWindow()
        {
            InitializeComponent();

            // Keep max/restore glyph in sync (safe if glyph not present)
            try { UpdateMaxGlyph(); StateChanged += (_, __) => UpdateMaxGlyph(); } catch { }

            // Determine mode
            bool firstRun = !File.Exists(_localDbPath);

            if (!firstRun)
            {
                // ==== Normal login ====
                // Hide verify + banner
                VerifyPasswordRow.Height = new GridLength(0);
                lblVerifyPassword.Visibility = Visibility.Collapsed;
                pbVerifyPassword.Visibility = Visibility.Collapsed;
                InfoBanner.Visibility = Visibility.Collapsed;

                // Hide verify caps warning (if present in XAML)
                HideCapsWarningFor(pbVerifyPassword);

                // Change button label only (keep the key glyph)
                try { tbKeyButtonText.Text = "Select Key File"; } catch { /* ignore */ }

                // Hide strength panel completely & detach handlers
                var strengthPanel = PwStrengthBar?.Parent as FrameworkElement;
                if (strengthPanel != null) strengthPanel.Visibility = Visibility.Collapsed;
                if (pbPassword != null) pbPassword.PasswordChanged -= Password_Changed;
                if (pbVerifyPassword != null) pbVerifyPassword.PasswordChanged -= VerifyPassword_Changed;
            }
            else
            {
                // ==== First run ====
                try { tbKeyButtonText.Text = "Create Key File"; } catch { /* ignore */ }

                // Wire advisory meter (no policy gating)
                if (pbPassword != null) pbPassword.PasswordChanged += Password_Changed;
                if (pbVerifyPassword != null) pbVerifyPassword.PasswordChanged += VerifyPassword_Changed;
            }

            // Ensure warnings start hidden
            HideCapsWarningFor(pbPassword);
            HideCapsWarningFor(pbVerifyPassword);
        }

        // =========================
        // Caps Lock warning handlers
        // =========================
        private void PasswordBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is not PasswordBox pb) return;

            ResetCapsWarnSession(pb);

            // Show immediately on focus if CapsLock is already on
            UpdateCapsWarning(pb);
        }

        private void PasswordBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is not PasswordBox pb) return;

            // Hide on blur
            HideCapsWarningFor(pb);
            ResetCapsWarnSession(pb);
        }

        private void PasswordBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not PasswordBox pb) return;

            // Update on any key press; especially relevant for CapsLock toggle or first typing.
            // If the user toggles CapsLock while focused, we reflect it immediately.
            UpdateCapsWarning(pb);
        }

        private void ResetCapsWarnSession(PasswordBox pb)
        {
            if (pb == pbPassword) _capsWarnShownForPassword = false;
            else if (pb == pbVerifyPassword) _capsWarnShownForVerify = false;
        }

        private void UpdateCapsWarning(PasswordBox pb)
        {
            // Only for these two password fields; ignore any other PasswordBox that may exist.
            if (pb != pbPassword && pb != pbVerifyPassword) return;

            bool capsOn = Keyboard.IsKeyToggled(Key.CapsLock);
            bool hasFocus = pb.IsKeyboardFocusWithin;

            if (!hasFocus || !capsOn)
            {
                HideCapsWarningFor(pb);
                return;
            }

            // Caps is on and field has focus: show once per focus session (no flicker).
            if (pb == pbPassword)
            {
                if (_capsWarnShownForPassword) return;
                ShowCapsWarning(tbCapsWarnPassword);
                _capsWarnShownForPassword = true;
            }
            else // pbVerifyPassword
            {
                if (_capsWarnShownForVerify) return;
                ShowCapsWarning(tbCapsWarnVerify);
                _capsWarnShownForVerify = true;
            }
        }

        private void HideCapsWarningFor(PasswordBox? pb)
        {
            if (pb == null) return;

            if (pb == pbPassword)
            {
                HideCapsWarning(tbCapsWarnPassword);
                _capsWarnShownForPassword = false;
            }
            else if (pb == pbVerifyPassword)
            {
                HideCapsWarning(tbCapsWarnVerify);
                _capsWarnShownForVerify = false;
            }
        }

        private static void ShowCapsWarning(TextBlock? tb)
        {
            if (tb == null) return;
            tb.Visibility = Visibility.Visible;
        }

        private static void HideCapsWarning(TextBlock? tb)
        {
            if (tb == null) return;
            tb.Visibility = Visibility.Collapsed;
        }

        // =========================
        // Title bar & window chrome
        // =========================
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (e.ClickCount == 2) ToggleMaxRestore();
                else if (e.ButtonState == MouseButtonState.Pressed) DragMove();
            }
            catch { /* ignore drag exceptions */ }
        }

        private void MinButton_Click(object sender, RoutedEventArgs e)
            => SystemCommands.MinimizeWindow(this);

        private void MaxRestoreButton_Click(object sender, RoutedEventArgs e)
            => ToggleMaxRestore();

        private void CloseButton_Click(object sender, RoutedEventArgs e)
            => SystemCommands.CloseWindow(this);

        private void ToggleMaxRestore()
        {
            if (WindowState == WindowState.Maximized)
                SystemCommands.RestoreWindow(this);
            else
                SystemCommands.MaximizeWindow(this);
            UpdateMaxGlyph();
        }

        private void UpdateMaxGlyph()
        {
            try
            {
                var tb = FindName("TbMaxGlyph") as TextBlock;
                if (tb != null)
                    tb.Text = (WindowState == WindowState.Maximized) ? "\uE923" : "\uE922"; // Restore / Maximize
            }
            catch { /* fine if glyph not present */ }
        }

        // =========================
        // Key file selection (open/save)
        // =========================
        private void btnCreateKeyFile_Click(object sender, RoutedEventArgs e)
        {
            if (File.Exists(_localDbPath))
            {
                // Existing DB: choose an existing encrypted key file
                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Select your encrypted key file",
                    Filter = "Key files (*.pv;*.kf;*.db;*.7z)|*.pv;*.kf;*.db;*.7z|SQLite key files (*.pv;*.kf;*.db)|*.pv;*.kf;*.db|Legacy archives (*.7z)|*.7z|All files (*.*)|*.*",
                    InitialDirectory = AppPaths.LocalAppDataRoot(),
                    CheckFileExists = true,
                    CheckPathExists = true,
                    Multiselect = false
                };

                if (openFileDialog.ShowDialog() == true)
                    tbKeyFile.Text = openFileDialog.FileName;
            }
            else
            {
                // First run: choose where to create the key file
                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Save your encrypted key file",
                    Filter = "Key files (*.pv;*.kf;*.db;*.7z)|*.pv;*.kf;*.db;*.7z|SQLite key files (*.pv;*.kf;*.db)|*.pv;*.kf;*.db|Legacy archives (*.7z)|*.7z|All files (*.*)|*.*",
                    FileName = "Kb.pv",
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    AddExtension = false,
                    OverwritePrompt = true
                };

                if (saveDialog.ShowDialog() == true)
                    tbKeyFile.Text = saveDialog.FileName;
            }
        }

        // =========================
        // Submit / Cancel
        // =========================
        private void btnSubmit_Click(object sender, RoutedEventArgs e)
        {
            // All secrets travel as SecureString/char[]; any string use is tightly scoped & dropped immediately.
            char[]? pwChars = null;
            char[]? verifyChars = null;
            char[]? newDbPw = null;
            string? keyArchivePath = null; // non-sensitive

            try
            {
                // clear any stale error first
                SurfaceError(null);

                keyArchivePath = tbKeyFile.Text;

                if (!File.Exists(_localDbPath))
                {
                    // --- First run (DB doesn't exist) ---
                    pwChars = SecureStringToChars(pbPassword.SecurePassword);
                    verifyChars = SecureStringToChars(pbVerifyPassword.SecurePassword);

                    if (!ValidateFirstRunInputs(pwChars, verifyChars, keyArchivePath, out string inputError))
                    {
                        SurfaceError(inputError);
                        return;
                    }

                    // Store key file path (non-sensitive) and password (sensitive)
                    SecureEncryptedDataStore.SetString(Key_KeyFile, keyArchivePath);
                    SecureEncryptedDataStore.SetAndWipe(Key_KeyPW, pwChars);
                    pwChars = Array.Empty<char>(); // defensive; already wiped by SetAndWipe

                    // Generate DB password (char[]), store, then wipe our copy
                    SecurePassword.Generate(ref newDbPw, 32);
                    SecureEncryptedDataStore.SetNoWipe(Key_DBPassword, newDbPw);
                    SensitiveDataCleaner.Clear(ref newDbPw);

                    // Setup DB and key file
                    var service = new ServiceSetUp();

                    string resultDb = service.SetUpDataBase();
                    if (string.Equals(resultDb, "error", StringComparison.OrdinalIgnoreCase))
                    {
                        SurfaceLoggedError("Database creation failed.");
                        return;
                    }

                    string resultKey = service.SetUpKeyFile();
                    if (resultKey.StartsWith("Error", StringComparison.OrdinalIgnoreCase))
                    {
                        SurfaceLoggedError("Key archive creation failed.");
                        return;
                    }
                }
                else
                {
                    // --- Existing DB (user selects key file + enters password) ---
                    if (string.IsNullOrWhiteSpace(keyArchivePath))
                    {
                        SurfaceError("Please select key file location.");
                        return;
                    }

                    pwChars = SecureStringToChars(pbPassword.SecurePassword);
                    if (pwChars.Length == 0)
                    {
                        SurfaceError("Please enter a password.");
                        return;
                    }

                    // Verify SQLite key file/password before storing credentials in SEDS.
                    if (!ValidateExistingSqliteKeyFile(keyArchivePath, pwChars, out string validationReason))
                    {
                        EarlyLoginFailures.Record(
                            EDT.InvalidPasswordOrKeyFile,
                            $"Invalid SQLite key-file password, schema, or payload. Path='{keyArchivePath}'. Reason='{validationReason}'"
                        );

                        SurfaceLoggedError("Invalid Key File Password or invalid key file selected.");
                        return;
                    }

                    // Store verified key file path (non-sensitive) and password (sensitive)
                    SecureEncryptedDataStore.SetString(Key_KeyFile, keyArchivePath);
                    SecureEncryptedDataStore.SetAndWipe(Key_KeyPW, pwChars);
                    pwChars = Array.Empty<char>(); // defensive; already wiped by SetAndWipe

                    // === Read-only JSON/base64 validation — existing keyfile ONLY ===
                    var service = new ServiceSetUp();
                    bool jsonOk = KeyProvisioner.ValidateKeysetJson(service.LoadKeysetJsonBytes);

                    if (!jsonOk)
                    {
                        _ = FatalErrorPopupHelper.ShowFatalAsync(
                            "The selected key file is corrupt and the application must close.",
                            details: "Read-only keyset validation failed for keyset.json after archive verification.");
                        return;
                    }
                }

                // 🔐 UI-only cleanup
                UICleaner.Clear(tbKeyFile);
                UICleaner.Clear(pbPassword);
                UICleaner.Clear(pbVerifyPassword);

                // Hide caps warnings after clearing
                HideCapsWarningFor(pbPassword);
                HideCapsWarningFor(pbVerifyPassword);

                // ✅ Load keys for this session
                try
                {
                    ServiceSetUp.EnsureKeySetFromArchive();
                }
                catch (Exception ex)
                {
                    _ = FatalErrorPopupHelper.ShowFatalAsync(
                        "MWPV could not load the required encryption keys and must close.",
                        ex,
                        "Session key material could not be loaded from the verified key archive.");
                    return;
                }

                if (MWPV.AppRunState.StartupContext.RunMode == AppRunMode.Upgrade)
                {
                    var upgradeResult = new AppUpgradeCoordinator()
                        .RunAuthenticatedUpgrade(MWPV.AppRunState.StartupContext);

                    AppExit.Set(upgradeResult.FinalExitCode, upgradeResult.Message);

                    if (!upgradeResult.Succeeded)
                    {
                        SurfaceLoggedError(upgradeResult.Message);
                        AppExit.Shutdown(Application.Current, upgradeResult.FinalExitCode, upgradeResult.Message);
                        return;
                    }

                    if (MWPV.AppRunState.StartupContext.ShouldExitAfterUpgrade)
                    {
                        AppExit.Shutdown(Application.Current, AppExitCode.Success, upgradeResult.Message);
                        return;
                    }
                }

                // 📦 Load additional SQL logic from key archive
                try
                {
                    SqlCagegory.EnsureKeysAndLoadAll();
                }
                catch (Exception ex)
                {
                    _ = FatalErrorPopupHelper.ShowFatalAsync(
                        "MWPV could not load the required SQL resources and must close.",
                        ex,
                        "Required SQL content could not be loaded after successful login.");
                    return;
                }

                // 🚨 Guard: must-have scripts check
                var missing = SqlCagegory.GetMissingMustHaves();
                if (missing.Length > 0)
                {
                    SurfaceLoggedError(
                        "Required SQL scripts are missing from the key archive: " +
                        string.Join(", ", missing)
                    );
                    // keep the window open for correction
                    return;
                }

                // ✅ Valid login complete, DB open, SQL loaded: write SESSION_START
                try { LogCatalogService.InsertSessionStart(); } catch { /* best-effort */ }
                MWPV.AppRunState.DbOpenedThisRun = true;  // <<< ONLY ADDED LINE

                // ✅ Close the form
                DialogResult = true;
                Close();
            }
            finally
            {
                // Memory hygiene: zero transient buffers
                if (pwChars != null) Array.Clear(pwChars, 0, pwChars.Length);
                if (verifyChars != null) Array.Clear(verifyChars, 0, verifyChars.Length);
                if (newDbPw != null) Array.Clear(newDbPw, 0, newDbPw.Length);
                keyArchivePath = null;
            }
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            // Scrub UI controls on cancel (UI-only)
            UICleaner.Clear(pbPassword);
            UICleaner.Clear(pbVerifyPassword);
            UICleaner.Clear(tbKeyFile);

            HideCapsWarningFor(pbPassword);
            HideCapsWarningFor(pbVerifyPassword);

            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            // Safety net: scrub even if user clicks the window X (UI-only)
            try
            {
                UICleaner.Clear(pbPassword);
                UICleaner.Clear(pbVerifyPassword);
                UICleaner.Clear(tbKeyFile);

                HideCapsWarningFor(pbPassword);
                HideCapsWarningFor(pbVerifyPassword);
            }
            catch { /* swallow */ }
            base.OnClosed(e);
        }

        // ========= Validation (char[]-based, no strings required) =========

        private static bool ValidateFirstRunInputs(char[] pw, char[] verify, string? keyPath, out string error)
        {
            error = null;

            if (pw == null || pw.Length == 0)
            {
                error = "Please enter a password.";
                return false;
            }
            if (verify == null || verify.Length == 0)
            {
                error = "Please re-enter the password to verify.";
                return false;
            }
            if (!FixedTimeEquals(pw, verify))
            {
                error = "Passwords do not match.";
                return false;
            }
            if (pw.Length < 8) // baseline; adjust as desired
            {
                error = "Password must be at least 8 characters.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(keyPath))
            {
                error = "Please select key file location.";
                return false;
            }
            return true;
        }

        private static bool FixedTimeEquals(char[] a, char[] b)
        {
            if (a == null || b == null) return false;
            int len = a.Length;
            if (b.Length != len) return false;
            int diff = 0;
            for (int i = 0; i < len; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        private static bool ValidateExistingSqliteKeyFile(string keyFilePath, char[] keyPassword, out string reason)
        {
            reason = string.Empty;

            if (string.IsNullOrWhiteSpace(keyFilePath))
            {
                reason = "Key file path is empty.";
                return false;
            }

            if (!File.Exists(keyFilePath))
            {
                reason = "Key file does not exist.";
                return false;
            }

            if (keyPassword == null || keyPassword.Length == 0)
            {
                reason = "Key file password is empty.";
                return false;
            }

            byte[]? payloadBytes = null;
            try
            {
                string? directory = Path.GetDirectoryName(keyFilePath);
                string fileName = Path.GetFileName(keyFilePath);

                if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
                {
                    reason = "Key file path is invalid.";
                    return false;
                }

                if (!KeyFileStore.CanOpenAndValidateSchema(directory, fileName, keyPassword, out reason))
                    return false;

                payloadBytes = KeyFileStore.ReadPayloadBytes(directory, fileName, keyPassword, payloadId: 1);
                if (payloadBytes.Length == 0)
                {
                    reason = "Key file payload row 1 is empty.";
                    return false;
                }

                byte[] payloadForValidation = payloadBytes;
                if (!KeyProvisioner.ValidateKeysetJson(() => payloadForValidation))
                {
                    reason = "Key file payload row 1 is not a valid keyset.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
            finally
            {
                if (payloadBytes != null)
                    Array.Clear(payloadBytes, 0, payloadBytes.Length);
            }
        }

        // ========= SecureString helpers =========

        private static char[] SecureStringToChars(SecureString? ss)
        {
            if (ss == null || ss.Length == 0) return Array.Empty<char>();

            IntPtr bstr = IntPtr.Zero;
            try
            {
                bstr = Marshal.SecureStringToBSTR(ss);
                int len = ss.Length;
                var chars = new char[len];

                // BSTR is UTF-16; read 2 bytes per char
                for (int i = 0; i < len; i++)
                {
                    short val = Marshal.ReadInt16(bstr, i * 2);
                    chars[i] = (char)val;
                }

                return chars;
            }
            finally
            {
                if (bstr != IntPtr.Zero)
                    Marshal.ZeroFreeBSTR(bstr);
            }
        }

        // =========================
        // Advisory strength meter wiring (first-run only)
        // =========================
        private void Password_Changed(object? sender, RoutedEventArgs e)
        {
            char[] buf = Array.Empty<char>();
            try
            {
                buf = SecureStringToChars(pbPassword?.SecurePassword);
                var result = PasswordStrengthEvaluator.Evaluate(buf);

                if (PwStrengthBar != null)
                    PwStrengthBar.Value = result.Score01;

                if (PwStrengthText != null)
                    PwStrengthText.Text = $"Password strength: {result.Strength} ({result.Length} chars)";

                if (PwTipsList != null)
                    PwTipsList.ItemsSource = result.Suggestions;

                if (PwStrengthBar != null)
                {
                    var brushKey = result.Strength switch
                    {
                        PasswordStrength.VeryWeak => "PwWeak",
                        PasswordStrength.Weak => "PwWeak",
                        PasswordStrength.Fair => "PwFair",
                        PasswordStrength.Strong => "PwStrong",
                        PasswordStrength.VeryStrong => "PwVeryStrong",
                        _ => "PwStrong"
                    };
                    if (TryFindResource(brushKey) is Brush b)
                        PwStrengthBar.Foreground = b;
                }

                // Clear visible mismatch message as the user types if now matching
                if (tbErrorMessage != null && tbErrorMessage.Visibility == Visibility.Visible)
                {
                    if (PasswordsMatchSecure())
                        SurfaceError(null);
                }
            }
            finally
            {
                if (buf.Length > 0) Array.Clear(buf, 0, buf.Length);
            }
        }

        private void VerifyPassword_Changed(object? sender, RoutedEventArgs e)
        {
            // Advisory-only: surface mismatch message; does not block submit
            if (!PasswordsMatchSecure())
                SurfaceError("Passwords do not match.");
            else
                SurfaceError(null);
        }

        private bool PasswordsMatchSecure()
        {
            if (pbPassword == null || pbVerifyPassword == null) return true;
            var a = pbPassword.SecurePassword;
            var b = pbVerifyPassword.SecurePassword;
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;

            IntPtr pa = IntPtr.Zero, pb = IntPtr.Zero;
            try
            {
                pa = Marshal.SecureStringToGlobalAllocUnicode(a);
                pb = Marshal.SecureStringToGlobalAllocUnicode(b);
                int bytes = a.Length * 2;
                int diff = 0;
                for (int i = 0; i < bytes; i += 2)
                {
                    diff |= Marshal.ReadInt16(pa, i) ^ Marshal.ReadInt16(pb, i);
                }
                return diff == 0;
            }
            finally
            {
                if (pa != IntPtr.Zero) Marshal.ZeroFreeGlobalAllocUnicode(pa);
                if (pb != IntPtr.Zero) Marshal.ZeroFreeGlobalAllocUnicode(pb);
            }
        }

        // =========================
        // Local helpers
        // =========================
        private void SurfaceError(string? message)
        {
            if (tbErrorMessage == null) return;

            if (string.IsNullOrWhiteSpace(message))
            {
                tbErrorMessage.Text = string.Empty;
                tbErrorMessage.Visibility = Visibility.Collapsed;
            }
            else
            {
                tbErrorMessage.Text = message;
                tbErrorMessage.Visibility = Visibility.Visible;
            }
        }

        private void SurfaceLoggedError(string baseMessage)
        {
            SurfaceError($"{baseMessage}\n\n(This error has been logged.)");
        }
    }
}
