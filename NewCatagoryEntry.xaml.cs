using MWPV.Services;
using System;
using System.Windows;
using System.Windows.Input;
using Utilities.Security;   // centralized input guards

namespace MWPV
{
    public partial class NewCategoryEntry : Window
    {
        private readonly MainWindow _mainWindow;

        public NewCategoryEntry(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;
        }

        /* ---------------- UI EVENTS ---------------- */

        private void btnAddCategory_Click(object sender, RoutedEventArgs e)
        {
            ClearError();

            // NAME: strict rules (min 4, max 17)
            var nameRes = InputGuards.ValidateCategoryName(tbCategoryName?.Text, minLen: 4, maxLen: 17);
            if (!nameRes.IsValid)
            {
                FailAndFocus(nameRes.Error ?? "Invalid category name.");
                return;
            }
            string name = nameRes.CleanName!;

            // DESCRIPTION: freeform (max 512, allow line breaks).
            // We still block a small set of dangerous symbols for tooltips/labels.
            string rawDesc = tbCategoryDescription?.Text ?? string.Empty;
            if (ContainsForbiddenFreeText(rawDesc))
            {
                Fail("Contains characters that aren’t allowed in description (e.g., angle brackets, pipe).");
                tbCategoryDescription?.Focus();
                return;
            }
            string? description = InputGuards.NormalizeFreeText(rawDesc, 512);
            if (string.IsNullOrWhiteSpace(description))
                description = name; // never store empty tooltip

            // Duplicate check (case-insensitive at DB layer)
            try
            {
                if (CategoryService.DoesCatagoryExist(name))
                {
                    FailAndFocus("Category already exists. Please enter a different name.");
                    return;
                }
            }
            catch (Exception ex)
            {
                FailAndFocus($"Error checking duplicates: {ex.Message}");
                return;
            }

            // Insert
            try
            {
                CategoryService.InsertCategory(name, description);
                _mainWindow.Panel.RefreshCategoryGrid();

                try { this.DialogResult = true; } catch { /* not shown as dialog */ }
                this.Close();
            }
            catch (Exception ex)
            {
                Fail($"Error inserting category: {ex.Message}");
            }
        }

        // Title bar drag/max/restore
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
            catch { }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            try { this.DialogResult = false; } catch { }
            this.Close();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            tbCategoryName?.Focus();
            tbCategoryName?.SelectAll();
        }

        private void tbCategoryName_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => ClearError();
        private void tbCategoryDescription_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => ClearError();

        /* ---------------- HELPERS ---------------- */

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

        private void FailAndFocus(string message)
        {
            ShowError(message);
            tbCategoryName?.Focus();
            tbCategoryName?.SelectAll();
        }

        private void Fail(string message) => ShowError(message);

        // Keep UI message consistent with server-side rules; tighten/loosen as needed.
        private static bool ContainsForbiddenFreeText(string? s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s)
            {
                switch (c)
                {
                    case '<':
                    case '>':
                    case '|':
                    case '`':
                    case '\\':
                    case '\'':
                    case '\"':
                    case ';':
                        return true;
                }
            }
            return false;
        }
    }
}