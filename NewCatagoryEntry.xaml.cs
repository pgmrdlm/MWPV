using MWPV.Services;
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;                 // VisualTreeHelper
using Utilities.Security;                  // centralized input guards
// NOTE: no "using MWPV.View.UserControls;" to avoid Panel ambiguity

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
            string rawDesc = tbCategoryDescription?.Text ?? string.Empty;
            if (ContainsForbiddenFreeText(rawDesc))   // TODO: replace with InputGuards on merge
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

                // Close first, then refresh the left panel’s category grid.
                try { this.DialogResult = true; } catch { /* not shown as dialog */ }
                this.Close();

                // Refresh after the window closes to avoid focus/timing glitches.
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var panel = TryGetPanelFromMainWindow(_mainWindow);
                    panel?.RefreshCategoryGrid();
                }), System.Windows.Threading.DispatcherPriority.Background);
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

        // TEMP guard; replace with InputGuards on merge.
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

        /// <summary>
        /// Try to obtain the left-side Panel control hosted in MainWindow.
        /// </summary>
        private static MWPV.View.UserControls.Panel? TryGetPanelFromMainWindow(MainWindow mw)
        {
            if (mw == null) return null;

            // 1) Public property named Panel (common pattern)
            try
            {
                var prop = typeof(MainWindow).GetProperty("Panel");
                if (prop != null)
                {
                    var val = prop.GetValue(mw) as MWPV.View.UserControls.Panel;
                    if (val != null) return val;
                }
            }
            catch { }

            // 2) Find by element name "Panel" in the namescope
            try
            {
                var byName = mw.FindName("Panel") as MWPV.View.UserControls.Panel;
                if (byName != null) return byName;
            }
            catch { }

            // 3) Visual tree crawl for first Panel instance
            try
            {
                return FindChild<MWPV.View.UserControls.Panel>(mw);
            }
            catch { }

            return null;
        }

        private static T? FindChild<T>(DependencyObject? parent) where T : DependencyObject
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T tChild) return tChild;
                var result = FindChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }
    }
}
