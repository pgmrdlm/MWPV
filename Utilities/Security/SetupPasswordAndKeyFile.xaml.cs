using System;
using System.IO;
using System.Reflection;              // (ok if not used after your edits)
using System.Runtime.InteropServices; // SecureString marshal
using System.Security;                // SecureString
using System.Windows;
using System.Windows.Input;

using Microsoft.Data.Sqlite;          // ok if not used directly here

using Utilities.Helpers;              // DatabaseHelper, ErrorHandler
using Utilities.Sql;                  // SqlCatagory, SchemaBootstrap
using Utilities.Security;             // SensitiveDataCleaner, SecureEncryptedDataStore, SecurePassword, ServiceSetUp, KeyArchiveVerifier
using Utilities.Diagnostics;          // EarlyLoginFailures, EarlyFailType, SmokeTester (DEBUG-only)
using EDT = Utilities.Diagnostics.EarlyFailType;

namespace Utilities.Security
{
    /// <summary>
    /// Setup dialog that handles both first-run provisioning and subsequent logins.
    /// Responsibilities:
    ///  - First run: create DB, build encrypted archive, stash DB password + keyset in the archive.
    ///  - Subsequent runs: verify an existing encrypted archive + password using sentinel files.
    ///  - Load SQL catalog from the archive and ensure Logs schema exists.
    ///  - (DEBUG) run a small smoke test.
    ///
    /// IMPORTANT: No secure DB logging or early-log ingestion is done here.
    ///            App.OnStartup performs SecureLogService.Initialize and then ingests early logs.
    /// </summary>
    public partial class SetupPasswordAndKeyFile : Window
    {
        // 🔑 SecureEncryptedDataStore key names (match files/keys inside the archive)
        private const string Key_DBPassword = "DB_Password.txt"; // file name inside the archive
        private const string Key_KeyFile = "KeyFile";            // non-sensitive path
        private const string Key_KeyPW = "KeyPW";                // sensitive password

        // Full path to local encrypted database
        private readonly string _localDbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MWPV",
            "MWPV.db"
        );

        public SetupPasswordAndKeyFile()
        {
            InitializeComponent();

            // Keep max/restore glyph in sync if custom titlebar is present
            try
            {
                UpdateMaxGlyph();
                StateChanged += (_, __) => UpdateMaxGlyph();
            }
            catch { /* fine if glyph not present */ }

            // Existing install: hide verify field, switch button wording
            if (File.Exists(_localDbPath))
            {
                VerifyPasswordRow.Height = new GridLength(0);
                lblVerifyPassword.Visibility = Visibility.Collapsed;
                pbVerifyPassword.Visibility = Visibility.Collapsed;
                btnCreateKeyFile.Content = "Select Key File";
            }
        }

        private void btnCreateKeyFile_Click(object sender, RoutedEventArgs e)
        {
            if (File.Exists(_localDbPath))
            {
                // Existing DB: choose an existing encrypted key archive (any extension acceptable)
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

                    // Store the key-archive password into the store (datastore wipes our array)
                    SecureEncryptedDataStore.SetAndWipe(Key_KeyPW, pwChars);
                    pwChars = Array.Empty<char>(); // defensive; already wiped by SetAndWipe

                    // Generate DB password (char[]), store, then wipe our copy
                    SecurePassword.Generate(ref newDbPw, 32); // your existing helper
                    SecureEncryptedDataStore.SetNoWipe(Key_DBPassword, newDbPw);
                    SensitiveDataCleaner.Clear(ref newDbPw);

                    // Setup DB and key file
                    var service = new ServiceSetUp();

                    string resultDb = service.SetUpDataBase();
                    if (string.Equals(resultDb, "error", StringComparison.OrdinalIgnoreCase))
                    {
                        ErrorHandler.InfoTitled("Setup",
                            "Database creation failed.\n\n(This error has been logged.)",
                            "SetupPasswordAndKeyFile/SetUpDataBase");
                        return;
                    }

                    string resultKey = service.SetUpKeyFile();
                    if (resultKey.StartsWith("Error", StringComparison.OrdinalIgnoreCase))
                    {
                        ErrorHandler.InfoTitled("Setup",
                            "Key archive creation failed.\n\n(This error has been logged.)",
                            "SetupPasswordAndKeyFile/SetUpKeyFile");
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

                    // Convert to char[] for memory hygiene
                    pwChars = SecureStringToChars(pbPassword.SecurePassword);
                    if (pwChars.Length == 0)
                    {
                        tbErrorMessage.Text = "Please enter a password.";
                        tbErrorMessage.Visibility = Visibility.Visible;
                        return;
                    }

                    // Unfortunately KeyArchiveVerifier currently needs a string.
                    // We scope a temporary string, call, then drop the reference.
                    string? pwTemp = null;
                    bool isCorrect = false;
                    try
                    {
                        pwTemp = new string(pwChars);
                        isCorrect = KeyArchiveVerifier.VerifyPasswordAndSentinels(keyArchivePath, pwTemp);
                    }
                    catch (Exception ex)
                    {
                        // EARLY LOG POINT #1 - verification threw (unexpected)
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
                        // Drop the string ref ASAP (cannot zeroize strings)
                        pwTemp = null;
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

                // 🔐 Secure Cleanup of UI controls
                SensitiveDataCleaner.Clear(tbKeyFile);
                SensitiveDataCleaner.Clear(pbPassword);
                SensitiveDataCleaner.Clear(pbVerifyPassword);

                // ✅ Load keys for this session (archive already verified/unlocked or freshly created)
                ServiceSetUp.EnsureKeySetFromArchive();

                // 📦 Load additional SQL logic from key archive (single source of truth)
                SqlCatagory.EnsureKeysAndLoadAll();

                // 🚨 Guard: if any must-have scripts are missing, stop gracefully (prevents later crashes)
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

                    // Abort setup cleanly so startup can exit without throwing
                    DialogResult = false;
                    Close();
                    return;
                }

                // 🧱 Ensure Logs schema exists (Init + Indexes), idempotent.
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
                keyArchivePath = null; // non-sensitive; drop ref anyway
            }
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            // Scrub UI controls on cancel
            SensitiveDataCleaner.Clear(pbPassword);
            SensitiveDataCleaner.Clear(pbVerifyPassword);
            SensitiveDataCleaner.Clear(tbKeyFile);
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            // Safety net: scrub even if user clicks the window X
            try
            {
                SensitiveDataCleaner.Clear(pbPassword);
                SensitiveDataCleaner.Clear(pbVerifyPassword);
                SensitiveDataCleaner.Clear(tbKeyFile);
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
            if (pw.Length < 8) // baseline; you can tighten or add char[] rules
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

        // SAFE version (no 'unsafe' keyword needed)
        private static char[] SecureStringToChars(System.Security.SecureString? ss)
        {
            if (ss == null || ss.Length == 0) return Array.Empty<char>();

            IntPtr bstr = IntPtr.Zero;
            try
            {
                bstr = System.Runtime.InteropServices.Marshal.SecureStringToBSTR(ss);
                int len = ss.Length;
                var chars = new char[len];

                // BSTR is UTF-16; read 2 bytes per char
                for (int i = 0; i < len; i++)
                {
                    short val = System.Runtime.InteropServices.Marshal.ReadInt16(bstr, i * 2);
                    chars[i] = (char)val;
                }

                return chars;
            }
            finally
            {
                if (bstr != IntPtr.Zero)
                    System.Runtime.InteropServices.Marshal.ZeroFreeBSTR(bstr);
            }
        }

        // ===== Custom title bar handlers (safe no-ops if not present in XAML) =====

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (e.ClickCount == 2)
                    ToggleMaxRestore();
                else
                    DragMove();
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

        // Swap □ (E922) / ⇱ (E923) if you named the TextBlock "TbMaxGlyph" in XAML
        private void UpdateMaxGlyph()
        {
            try
            {
                var tb = FindName("TbMaxGlyph") as System.Windows.Controls.TextBlock;
                if (tb != null)
                    tb.Text = (WindowState == WindowState.Maximized) ? "\uE923" : "\uE922";
            }
            catch { /* fine if glyph not present */ }
        }
    }
}
