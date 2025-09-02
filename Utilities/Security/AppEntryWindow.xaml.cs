using System;
using System.IO;
using System.Runtime.InteropServices; // SecureString marshal
using System.Security;                // SecureString
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;           // Brush for meter coloring

using Microsoft.Data.Sqlite;          // ok if not used directly here

using Utilities.Helpers;              // DatabaseHelper, ErrorHandler
using Utilities.Sql;                  // SqlCatagory, SchemaBootstrap
using Security.Utility;               // SensitiveDataCleaner, SecureEncryptedDataStore, SecurePassword, ServiceSetUp, KeyArchiveVerifier
using Utilities.Diagnostics;          // EarlyLoginFailures, EarlyFailType, SmokeTester (DEBUG-only)
using EDT = Utilities.Diagnostics.EarlyFailType;
using Security.Utility.Crypto;        // KeyArchiveVerifier
using MWPV.Utilities.UI;              // UICleaner (UI-only scrubbers)
using MWPV.Utilities.Security;        // PasswordStrengthEvaluator, PasswordStrength

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
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MWPV",
            "MWPV.db"
        );

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

                // Change button label only (keep the key glyph)
                try { tbKeyButtonText.Text = "Select Key File"; } catch { /* if not found, ignore */ }

                // Hide strength panel completely & detach handlers
                var strengthPanel = PwStrengthBar?.Parent as FrameworkElement;
                if (strengthPanel != null) strengthPanel.Visibility = Visibility.Collapsed;
                if (pbPassword != null) pbPassword.PasswordChanged -= Password_Changed;
                if (pbVerifyPassword != null) pbVerifyPassword.PasswordChanged -= VerifyPassword_Changed;
            }
            else
            {
                // ==== First run ====
                // Ensure label is "Create Key File" (also set in XAML)
                try { tbKeyButtonText.Text = "Create Key File"; } catch { /* ignore */ }

                // Wire advisory meter (no policy gating)
                if (pbPassword != null) pbPassword.PasswordChanged += Password_Changed;
                if (pbVerifyPassword != null) pbVerifyPassword.PasswordChanged += VerifyPassword_Changed;
            }
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
                var tb = FindName("TbMaxGlyph") as System.Windows.Controls.TextBlock;
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
                // Existing DB: choose an existing encrypted key archive
                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Select your encrypted key archive",
                    Filter = "Archives & All files|*.7z;*.zip;*.*|All files (*.*)|*.*",
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    CheckFileExists = true,
                    CheckPathExists = true,
                    Multiselect = false
                };

                if (openFileDialog.ShowDialog() == true)
                    tbKeyFile.Text = openFileDialog.FileName;
            }
            else
            {
                // First run: choose where to create the archive
                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Save your encrypted key archive",
                    Filter = "7-Zip archive (*.7z)|*.7z|All files (*.*)|*.*",
                    FileName = "Key.7z",
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
                keyArchivePath = tbKeyFile.Text;

                if (!File.Exists(_localDbPath))
                {
                    // --- First run (DB doesn't exist) ---
                    pwChars = SecureStringToChars(pbPassword.SecurePassword);
                    verifyChars = SecureStringToChars(pbVerifyPassword.SecurePassword);

                    if (!ValidateFirstRunInputs(pwChars, verifyChars, keyArchivePath, out string inputError))
                    {
                        tbErrorMessage.Text = inputError;
                        tbErrorMessage.Visibility = Visibility.Visible;
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
                        ErrorHandler.InfoTitled("Setup",
                            "Database creation failed.\n\n(This error has been logged.)",
                            "AppEntryWindow/SetUpDataBase");
                        return;
                    }

                    string resultKey = service.SetUpKeyFile();
                    if (resultKey.StartsWith("Error", StringComparison.OrdinalIgnoreCase))
                    {
                        ErrorHandler.InfoTitled("Setup",
                            "Key archive creation failed.\n\n(This error has been logged.)",
                            "AppEntryWindow/SetUpKeyFile");
                        return;
                    }
                }
                else
                {
                    // --- Existing DB (user selects key file + enters password) ---
                    if (string.IsNullOrWhiteSpace(keyArchivePath))
                    {
                        tbErrorMessage.Text = "Please select key file location.";
                        tbErrorMessage.Visibility = Visibility.Visible;
                        return;
                    }

                    pwChars = SecureStringToChars(pbPassword.SecurePassword);
                    if (pwChars.Length == 0)
                    {
                        tbErrorMessage.Text = "Please enter a password.";
                        tbErrorMessage.Visibility = Visibility.Visible;
                        return;
                    }

                    // KeyArchiveVerifier needs a string (short-lived)
                    string? pwTemp = null;
                    bool isCorrect = false;
                    try
                    {
                        pwTemp = new string(pwChars);
                        isCorrect = KeyArchiveVerifier.VerifyPasswordAndSentinels(keyArchivePath, pwTemp);
                    }
                    catch (Exception ex)
                    {
                        EarlyLoginFailures.Record(
                            EDT.KeyFileVerifyError,
                            $"Key file verification threw: {ex.GetType().Name}: {ex.Message}"
                        );

                        ErrorHandler.InfoTitled("Key File Verification",
                            "Error verifying key file.\n\n(This error has been logged.)",
                            "KeyFileVerify");
                        tbErrorMessage.Text = "Error verifying key file.";
                        tbErrorMessage.Visibility = Visibility.Visible;
                        return;
                    }
                    finally
                    {
                        pwTemp = null; // drop string ref ASAP
                    }

                    if (!isCorrect)
                    {
                        EarlyLoginFailures.Record(
                            EDT.InvalidPasswordOrKeyFile,
                            $"Invalid key-file password or unsupported/unencrypted archive. Path='{keyArchivePath}'"
                        );

                        ErrorHandler.InfoTitled("Key File Verification",
                            "Invalid Key File Password or invalid key file selected.\n\n(This error has been logged.)",
                            "KeyFileVerify");

                        tbErrorMessage.Text = "Invalid Key File Password or invalid key file selected.";
                        tbErrorMessage.Visibility = Visibility.Visible;
                        return;
                    }

                    // Store verified key file path (non-sensitive) and password (sensitive)
                    SecureEncryptedDataStore.SetString(Key_KeyFile, keyArchivePath);
                    SecureEncryptedDataStore.SetAndWipe(Key_KeyPW, pwChars);
                    pwChars = Array.Empty<char>(); // defensive; already wiped by SetAndWipe
                }

                // 🔐 UI-only cleanup
                UICleaner.Clear(tbKeyFile);
                UICleaner.Clear(pbPassword);
                UICleaner.Clear(pbVerifyPassword);

                // ✅ Load keys for this session
                ServiceSetUp.EnsureKeySetFromArchive();

                // 📦 Load additional SQL logic from key archive
                SqlCatagory.EnsureKeysAndLoadAll();

                // 🚨 Guard: must-have scripts check
                var missing = SqlCatagory.GetMissingMustHaves();
                if (missing.Length > 0)
                {
#if DEBUG
                    System.Diagnostics.Debug.WriteLine("[SQLCAT][FATAL] Missing must-have scripts: " + string.Join(", ", missing));
#endif
                    ErrorHandler.InfoTitled(
                        "SQL Catalog",
                        "Required SQL scripts are missing from the key archive:\n" +
                        string.Join(", ", missing) +
                        "\n\nPlease verify you selected the correct encrypted archive. (This error has been logged.)",
                        "SQLCatalog.Missing"
                    );

                    DialogResult = false;
                    Close();
                    return;
                }

                // 🧱 Ensure Logs schema exists (idempotent)
                try
                {
                    using var openConn = DatabaseHelper.OpenConnection();
                    SchemaBootstrap.EnsureLogsSchema(openConn);

#if DEBUG
                    // 🔎 DEBUG-only smoke test
                    SmokeTester.Run();
#endif
                }
                catch (Exception ex)
                {
                    // Non-fatal: surface a friendly message and continue
                    ErrorHandler.InfoTitled(
                        "Schema Bootstrap",
                        $"Log schema bootstrap failed: {ex.Message}\n\n(This error has been logged.)",
                        "SchemaBootstrap.EnsureLogsSchema"
                    );
                }

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
                    {
                        tbErrorMessage.Text = string.Empty;
                        tbErrorMessage.Visibility = Visibility.Collapsed;
                    }
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
            {
                tbErrorMessage.Text = "Passwords do not match.";
                tbErrorMessage.Visibility = Visibility.Visible;
            }
            else
            {
                tbErrorMessage.Text = string.Empty;
                tbErrorMessage.Visibility = Visibility.Collapsed;
            }
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
    }
}
