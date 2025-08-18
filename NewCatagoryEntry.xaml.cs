using MWPV.Services;
using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;

namespace MWPV
{
    public partial class NewCategoryEntry : Window
    {
        private readonly MainWindow _mainWindow;

        // Block control chars except CR/LF/TAB
        private static readonly Regex _invalidNameChars =
            new Regex(@"[\p{C}&&[^\r\n\t]]", RegexOptions.Compiled);

        public NewCategoryEntry(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;
        }

        /* ---------------- UI EVENTS ---------------- */

        private void btnAddCategory_Click(object sender, RoutedEventArgs e)
        {
            ClearError();

            // Normalize inputs
            string name = (tbCategoryName.Text ?? string.Empty).Trim();
            string? description = NormalizeDescription(tbCategoryDescription?.Text);

            // If no description provided, default to the category name (avoid empty tooltips)
            if (string.IsNullOrWhiteSpace(description))
                description = name;

            // --- Validations ---
            if (name.Length < 4) { if (Fail("Category name must be at least 4 characters.")) return; }
            if (name.Length > 17) { if (Fail("Category name must be 17 characters or fewer.")) return; }
            if (_invalidNameChars.IsMatch(name))
            { if (Fail("Category name contains invalid characters.")) return; }

            // Duplicate check (case-insensitive via SQL)
            try
            {
                if (CategoryService.DoesCatagoryExist(name))
                {
                    if (Fail("Category already exists. Please enter a different name.")) return;
                }
            }
            catch (Exception ex)
            {
                if (Fail($"Error checking duplicates: {ex.Message}")) return;
            }

            // Insert
            try
            {
                CategoryService.InsertCategory(name, description);

                // Refresh and close
                _mainWindow.Panel.RefreshCategoryGrid();
                try { this.DialogResult = true; } catch { /* not shown as dialog */ }
                this.Close();
            }
            catch (Exception ex)
            {
                Fail($"Error inserting category: {ex.Message}");
            }
        }

        // Drag the custom title bar (same pattern as password window)
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (e.ClickCount == 2)
                {
                    WindowState = WindowState == WindowState.Maximized
                        ? WindowState.Normal
                        : WindowState.Maximized;
                }
                else if (e.ButtonState == MouseButtonState.Pressed)
                {
                    DragMove();
                }
            }
            catch { /* ignore drag exceptions */ }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            try { this.DialogResult = false; } catch { /* not a dialog; ignore */ }
            this.Close();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            tbCategoryName?.Focus();
            tbCategoryName?.SelectAll();
        }

        private void tbCategoryName_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
            => ClearError();

        private void tbCategoryDescription_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
            => ClearError();

        /* ---------------- HELPERS ---------------- */

        private static string? NormalizeDescription(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            string s = raw.Trim();

            // Enforce max length (keep in sync with UI/DB policy)
            if (s.Length > 512) s = s.Substring(0, 512);

            // Remove control chars except CR/LF/TAB
            s = Regex.Replace(s, @"[\p{C}&&[^\r\n\t]]", string.Empty);

            return string.IsNullOrWhiteSpace(s) ? null : s;
        }

        private void ClearError()
        {
            txtErrorMessage.Text = string.Empty;
            txtErrorMessage.Visibility = Visibility.Collapsed;
        }

        private void ShowError(string message)
        {
            txtErrorMessage.Text = message;
            txtErrorMessage.Visibility = Visibility.Visible;
        }

        // Allows terse: if (Fail("msg")) return;
        private bool Fail(string message)
        {
            ShowError(message);
            return true;
        }
    }
}