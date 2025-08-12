using System;
using System.IO;
using System.Windows;
using Utilities.Helpers;

namespace Utilities.Security
{
    public partial class SetupPasswordAndKeyFile : Window
    {
        // 🔑 SecureEncryptedDataStore key names
        private const string Key_DBPassword = "DB_Password.txt"; // matches the file name inside the archive
        private const string Key_KeyFile = "KeyFile";         // non-sensitive path
        private const string Key_KeyPW = "KeyPW";             // sensitive password

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
                // Existing DB: user is selecting an existing key file
                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Select your key file",
                    Filter = "7-Zip Archives (*.7z)|*.7z|All files (*.*)|*.*",
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    tbKeyFile.Text = openFileDialog.FileName;
                }
            }
            else
            {
                // No DB: user is creating a new key file
                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Save your Key File",
                    Filter = "7-Zip Archive (*.7z)|*.7z|All Files (*.*)|*.*",
                    FileName = "Key.7z",
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
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
                string resultKey = service.SetUpKeyFile();
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
                    tbErrorMessage.Text = "Please select Key File Location";
                    tbErrorMessage.Visibility = Visibility.Visible;
                    return;
                }

                fullPath = tbKeyFile.Text;

                bool isCorrect = false;
                try
                {
                    isCorrect = ServiceSetUp.VerifyKeyFilePW(fullPath, password);
                }
                catch (Exception ex)
                {
                    // EARLY LOG POINT #1 - verification threw
                    EarlyLoginFailures.Record(
                        "KeyFileVerifyError",
                        $"Key file verification threw: {ex.GetType().Name}: {ex.Message}"
                    );

                    tbErrorMessage.Text = "Error verifying key file.";
                    tbErrorMessage.Visibility = Visibility.Visible;
                    return;
                }

                if (!isCorrect)
                {
                    // EARLY LOG POINT #2 - wrong password / wrong file
                    EarlyLoginFailures.Record(
                        "InvalidPasswordOrKeyFile",
                        $"Invalid key-file password or wrong key file. Path='{fullPath ?? tbKeyFile.Text}'"
                    );

                    tbErrorMessage.Text = "Invalid Key File Password or Invalid Key File selected.";
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

            // 📦 Load additional SQL logic from key archive
            string[] strSqlite =
            {
                "CatagoryExists.sql",
                DatabaseHelper.DbPasswordKey,
                "InsertCatagory.sql",
                "SelectCatagories.sql",
                "Logs_Insert_V2.sql"
            };
            for (int i = 0; i < strSqlite.Length; i++)
            {
                ServiceSetUp.LoadSqlFromEncryptedArchive(strSqlite[i]);
            }

            // Success: keyfile password verified and DB connection is open
            if (EarlyLoginFailures.HasPending())
            {
                ErrorHandler.Info("Previous login failures were detected and will be logged.", "Login Notice");

                EarlyLoginFailures.FlushToDb(
                    (utc, type, detail) =>
                        SecureLogService.WriteAsync(
                            level: LogLevel.Info,
                            payload: new { earlyFail = type.ToString(), detail, occurredUtc = utc },
                            eventCode: "EARLY_LOGIN_FAILURE",
                            source: "SetupPasswordAndKeyFile"
                        ).GetAwaiter().GetResult(),
                    path => SensitiveDataCleaner.SecureFileDelete(path, overwritePasses: 1)
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
                error = "Please select Key File Location";
                return false;
            }

            return true;
        }
    }
}
