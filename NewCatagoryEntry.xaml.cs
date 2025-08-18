using MWPV.Services;
using System;
using System.Windows;
using System.Windows.Input;
using Utilities.Security;   // <- new centralized guards

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
            var nameRes = InputGuards.Validate(tbCategoryName?.Text, 4, 17);
            if (!nameRes.IsValid)
            {
                FailAndFocus(nameRes.Error ?? "Invalid category name.");
                return;
            }
            string name = nameRes.Clean!;

            // DESCRIPTION: freeform (max 512, allow line breaks)
            var descRes = InputGuards.Validate(tbCategoryDescription?.Text, 512, allowLineBreaks: true);
            if (!descRes.IsValid)
            {
                Fail(descRes.Error ?? "Invalid description.");
                tbCategoryDescription?.Focus();
                return;
            }

            string? description = string.IsNullOrWhiteSpace(descRes.Clean) ? name : descRes.Clean;

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
                try { this.DialogResult = true; } catch { }
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
                    WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
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
    }
}