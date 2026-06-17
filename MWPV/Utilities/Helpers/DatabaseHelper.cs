using Microsoft.Data.Sqlite;
using System;
using System.IO;
using System.Threading.Tasks;
using Security.Utility;           // SecureEncryptedDataStore, SensitiveDataCleaner
using Utilities.Helpers;          // ErrorHandler
using MWPV.Services.AppLifecycle;

namespace Utilities.Helpers
{
    public static class DatabaseHelper
    {
        // SINGLE SOURCE OF TRUTH for the stored DB password
        public const string DbPasswordKey = "DB_Password.txt";

        public static string GetAppDbPath() =>
            Path.Combine(AppPaths.LocalAppDataRoot(), "MWPV", "MWPV.db");

        public static string GetSqlFolderPath() =>
            Path.Combine(AppPaths.LocalAppDataRoot(), "MWPV", "sql");


        public static void StoreDatabasePassword(char[] password)
        {
            if (password == null || password.Length == 0)
                throw new ArgumentException("Password must not be empty.", nameof(password));
            SecureEncryptedDataStore.SetAndWipe(DbPasswordKey, password);
        }

        public static char[] ReadDatabasePassword() =>
            SecureEncryptedDataStore.GetChars(DbPasswordKey);

        public static void WithDatabasePasswordString(Action<string> use)
        {
            if (use == null) throw new ArgumentNullException(nameof(use));
            char[]? pwChars = null;
            string? pwString = null;
            try
            {
                pwChars = ReadDatabasePassword();
                if (pwChars == null || pwChars.Length == 0)
                    throw new InvalidOperationException("Database password not loaded.");
                pwString = new string(pwChars);
                use(pwString);
            }
            finally
            {
                if (pwString != null) SensitiveDataCleaner.WipeString(ref pwString);
                if (pwChars != null) SensitiveDataCleaner.WipeCharArray(pwChars);
            }
        }

        /// <summary>
        /// Open an **encrypted** app DB connection using the stored password.
        /// On any login/open failure, shows a standardized error and exits the app.
        /// </summary>
        public static SqliteConnection GetAppOpenConnection()
        {
            var dbPath = GetAppDbPath();
            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            SqliteConnection? conn = null;
            Exception? captured = null;

            WithDatabasePasswordString(pw =>
            {
                try
                {
                    var csb = new SqliteConnectionStringBuilder
                    {
                        DataSource = dbPath,
                        Mode = SqliteOpenMode.ReadWriteCreate,
                        Password = pw,  // SQLCipher-enabled build required
                    };

                    conn = new SqliteConnection(csb.ToString());
                    conn.Open();

                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
PRAGMA foreign_keys = ON;
PRAGMA secure_delete = ON;
PRAGMA journal_mode = WAL;";
                    cmd.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    captured = ex; // we’ll handle uniformly below
                }
            });

            if (captured != null)
            {
                ShowInvalidDbPasswordAndExit();
                AppExit.Shutdown(System.Windows.Application.Current, AppExitCode.StartupDatabaseOpenFailed, "Encrypted database open failed.");
                throw new InvalidOperationException("Encrypted database open failed.", captured);
            }

            if (conn == null)
            {
                ShowInvalidDbPasswordAndExit();
                AppExit.Shutdown(System.Windows.Application.Current, AppExitCode.StartupDatabaseOpenFailed, "Encrypted database open returned no connection.");
                throw new InvalidOperationException("Encrypted database open returned no connection.");
            }

            return conn;
        }

        public static SqliteConnection OpenConnection() => GetAppOpenConnection();

        public static async Task<T> WithConnectionAsync<T>(Func<SqliteConnection, Task<T>> work)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            await using var conn = GetAppOpenConnection();
            return await work(conn);
        }

        public static async Task WithConnectionAsync(Func<SqliteConnection, Task> work)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            await using var conn = GetAppOpenConnection();
            await work(conn);
        }

        // ---------- one standardized error dialog ----------
        private static void ShowInvalidDbPasswordAndExit()
        {
            const string title = "Encrypted Database Locked";
            const string message =
                "The app couldn’t open its encrypted database.\n\n" +
                "This almost always means the key file or password doesn’t match.\n\n" +
                "Select the correct key file and try again.";

            try { ErrorHandler.Abend(null, message, title); } catch { }
        }
    }
}
