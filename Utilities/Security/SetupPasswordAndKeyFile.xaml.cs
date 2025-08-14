using System;
using System.IO;
using System.Reflection;              // for app version
using System.Windows;

using Microsoft.Data.Sqlite;          // Microsoft provider (ok to keep even if not used directly here)

using Utilities.Helpers;              // DatabaseHelper, ErrorHandler
using Utilities.Sql;                  // SqlCatagory, SchemaBootstrap
using Utilities.Security;             // SensitiveDataCleaner, SecureEncryptedDataStore, SecurePassword, ServiceSetUp
using Utilities.Diagnostics;          // EarlyLoginFailures, EarlyFailType, SmokeTester (DEBUG-only)
using MWPV.Services;                  // LogRepository, LogLevel

namespace Utilities.Security
{
    /// <summary>
    /// Setup dialog that handles both first-run provisioning and subsequent logins.
    /// - First run: user picks archive path (any extension), sets a password; we create DB, build encrypted archive,
    ///   and store the canonical DB password file + keyset.json inside it.
    /// - Subsequent runs: user selects an existing archive and enters its password.
    ///   We verify with <see cref="ServiceSetUp.VerifyKeyFilePW"/> which requires:
    ///   (a) entries are encrypted, and (b) sentinel files exist (DB_Password.txt + keyset.json).
    ///   On failure, we create an early log (.elog) and show a friendly message.
    /// After success we load SQL from the archive, verify must-have scripts, ensure Logs schema,
    /// and (in DEBUG) run a small smoke test.
    /// </summary>
    public partial class SetupPasswordAndKeyFile : Window
    {
        // 🔑 SecureEncryptedDataStore key names
        private const string Key_DBPassword = "DB_Password.txt"; // matches the file name inside the archive
        private const string Key_KeyFile = "KeyFile";         // non-sensitive path
        private const string Key_KeyPW = "KeyPW";           // sensitive password

        // Full path to local encrypted database
        private readonly string localAppDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MWPV",
            "MWPV.db"
        );

        public SetupPasswordAndKeyFile()
        {
            InitializeComponent();

            // If database already exists, hide verification fields and change button label
            if (File.Exists(localAppDataPath))
            {
                VerifyPasswordRow.Height = new GridLength(0);
                lblVerifyPassword.Visibility = Visibility.Collapsed;
                pbVerifyPassword.Visibility = Visibility.Collapsed;
                btnCreateKeyFile.Content = "Select Key File";
            }
        }

        private void btnCreateKeyFile_Click(object sender, RoutedEventArgs e)
        {
            if (File.Exists(localAppDataPath))
            {
                // Existing DB: user is selecting an existing key file (extension-agnostic)
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
                {
                    tbKeyFile.Text = openFileDialog.FileName;
                }
            }
            else
            {
                // No DB: user is creating a new key file (let them choose any extension)
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
                {
                    tbKeyFile.Text = saveDialog.FileName;
                }
            }
        }

        private void btnSubmit_Click(object sender, RoutedEventArgs e)
        {
            string password = pbPassword.Password;          // NOTE: string; plan to migrate to SecureString/char[] later
            string verifyPassword = pbVerifyPassword.Password;
            string fullPath = null;

            if (!File.Exists(localAppDataPath))
            {
                // --- First run (DB doesn't exist) ---
                if (!ValidateFirstRunInputs(password, verifyPassword, out string inputError))
                {
                    tbErrorMessage.Text = inputError;
                    tbErrorMessage.Visibility = Visibility.Visible;
                    return;
                }

                fullPath = tbKeyFile.Text;

                // Store key file path (non-sensitive) and password (sensitive)
                SecureEncryptedDataStore.SetString(Key_KeyFile, fullPath);

                char[] pwChars = password.ToCharArray();
                SecureEncryptedDataStore.SetAndWipe(Key_KeyPW, pwChars); // datastore wipes pwChars
                SensitiveDataCleaner.WipeString(ref password);           // drop original string ref

                // Generate and store a secure database password — char[] only until storage
                char[] newPassword = null;
                SecurePassword.Generate(ref newPassword, 32);

                SecureEncryptedDataStore.SetNoWipe(Key_DBPassword, newPassword);
                SensitiveDataCleaner.WipeCharArray(newPassword);

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
                if (string.IsNullOrWhiteSpace(password))
                {
                    tbErrorMessage.Text = "Please enter a password.";
                    tbErrorMessage.Visibility = Visibility.Visible;
                    return;
                }

                if (string.IsNullOrWhiteSpace(tbKeyFile.Text))
                {
                    tbErrorMessage.Text = "Please select key file location.";
                    tbErrorMessage.Visibility = Visibility.Visible;
                    return;
                }

                fullPath = tbKeyFile.Text;

                bool isCorrect = false;
                try
                {
                    // Strict verify: encrypted entries + sentinel files (DB_Password.txt & keyset.json)
                    isCorrect = ServiceSetUp.VerifyKeyFilePW(fullPath, password);
                }
                catch (Exception ex)
                {
                    // EARLY LOG POINT #1 - verification threw (unexpected)
                    EarlyLoginFailures.Record(
                        EarlyFailType.KeyFileVerifyError,
                        $"Key file verification threw: {ex.GetType().Name}: {ex.Message}"
                    );

                    // Friendly notice for the user (elog will be ingested after login succeeds)
                    ErrorHandler.InfoTitled("Key File Verification",
                        "Error verifying key file.\n\n(This error has been logged.)",
                        "KeyFileVerify");
                    tbErrorMessage.Text = "Error verifying key file.";
                    tbErrorMessage.Visibility = Visibility.Visible;
                    return;
                }

                if (!isCorrect)
                {
                    // EARLY LOG POINT #2 - wrong password / wrong or unencrypted file / missing sentinels
                    EarlyLoginFailures.Record(
                        EarlyFailType.InvalidPasswordOrKeyFile,
                        $"Invalid key-file password or unsupported/unencrypted archive. Path='{fullPath ?? tbKeyFile.Text}'"
                    );

                    // Consistent helper popup
                    ErrorHandler.InfoTitled("Key File Verification",
                        "Invalid Key File Password or invalid key file selected.\n\n(This error has been logged.)",
                        "KeyFileVerify");

                    tbErrorMessage.Text = "Invalid Key File Password or invalid key file selected.";
                    tbErrorMessage.Visibility = Visibility.Visible;
                    return;
                }

                // Store verified key file path (non-sensitive) and password (sensitive)
                SecureEncryptedDataStore.SetString(Key_KeyFile, fullPath);

                char[] keyPwChars = password.ToCharArray();
                SecureEncryptedDataStore.SetAndWipe(Key_KeyPW, keyPwChars);
                SensitiveDataCleaner.WipeString(ref password);
            }

            // 🔐 Secure Cleanup of UI and memory
            SensitiveDataCleaner.Clear(tbKeyFile);
            SensitiveDataCleaner.Clear(pbPassword);
            SensitiveDataCleaner.Clear(pbVerifyPassword);

            SensitiveDataCleaner.WipeString(ref password);
            SensitiveDataCleaner.WipeString(ref verifyPassword);
            SensitiveDataCleaner.WipeString(ref fullPath);

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
                this.DialogResult = false;
                this.Close();
                return;
            }

            // 🧱 Ensure Logs schema exists (Init + Indexes), idempotent.
            // Uses your DatabaseHelper to open an already-keyed connection.
            try
            {
                using var openConn = DatabaseHelper.OpenConnection();
                SchemaBootstrap.EnsureLogsSchema(openConn);

#if DEBUG
                // 🔎 DEBUG-only smoke test: read categories + insert a log
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

            // Success: keyfile password verified and DB connection is open
            if (EarlyLoginFailures.HasPending())
            {
                ErrorHandler.InfoTitled(
                    "Login Notice",
                    "Previous login failures were detected and will be logged.",
                    "EarlyLogin.Ingest"
                );

                // Use LogRepository so FlushToDb knows success and deletes files on success
                var repo = new LogRepository(
                    Utilities.Helpers.DatabaseHelper.OpenConnection,
                    Assembly.GetEntryAssembly()?.GetName()?.Version?.ToString() ?? "dev"
                );

                EarlyLoginFailures.FlushToDb(
                    (utc, type, detail) =>
                        repo.LogAsync(
                            level: MWPV.Services.LogLevel.Info,
                            source: "SetupPasswordAndKeyFile",
                            eventCode: "EARLY_LOGIN_FAILURE",
                            payloadObject: new { earlyFail = type.ToString(), detail, occurredUtc = utc },
                            isCrash: false,
                            sessionId: null,
                            stackHash: null
                        ).GetAwaiter().GetResult() > 0,   // convert inserted row id -> success

                    path => { SensitiveDataCleaner.SecureFileDelete(path, overwritePasses: 1); return true; }
                );
            }

            // ✅ Close the form
            this.DialogResult = true;
            this.Close();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private bool ValidateFirstRunInputs(string password, string verifyPassword, out string error)
        {
            error = null;

            if (!SecurePassword.IsPasswordValid(password, verifyPassword, out var pwError))
            {
                error = pwError;
                return false;
            }

            if (string.IsNullOrWhiteSpace(tbKeyFile.Text))
            {
                error = "Please select key file location.";
                return false;
            }

            return true;
        }
    }
}
