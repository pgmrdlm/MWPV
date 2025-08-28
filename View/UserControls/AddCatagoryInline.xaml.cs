using System;
using System.Windows;
using System.Windows.Controls;
using MWPV.Services;          // CategoryService
using Security.Utility;     // InputGuards

namespace MWPV.View.UserControls
{
    public partial class AddCatagoryInline : UserControl
    {
        // Events the host (Panel) can subscribe to
        public event EventHandler<CatagorySubmittedEventArgs>? Submitted;
        public event EventHandler? Canceled;

        public AddCatagoryInline()
        {
            InitializeComponent();
            Loaded += (s, e) => tbCategoryName?.Focus();
        }

        /* ---------------- UI EVENTS ---------------- */

        private void tbCategoryName_TextChanged(object sender, TextChangedEventArgs e)
        {
            // keep UX snappy: clear previous errors as the user types
            ClearError();
        }

        private void tbCategoryDescription_TextChanged(object sender, TextChangedEventArgs e)
        {
            ClearError();
        }

        private void btnAddCategory_Click(object sender, RoutedEventArgs e)
        {
            ClearError();
            SetBusy(true);

            try
            {
                // NAME: strict rules (min 4, max 17) using your central guard
                var nameRes = InputGuards.ValidateCategoryName(tbCategoryName?.Text, minLen: 4, maxLen: 17);
                if (!nameRes.IsValid)
                {
                    FailAndFocus(nameRes.Error ?? "Invalid category name.");
                    return;
                }
                string name = nameRes.CleanName!;

                // DESCRIPTION: centralized validation + normalization (max 512)
                var descRes = InputGuards.ValidateDescription(tbCategoryDescription?.Text, maxLen: 512);
                if (!descRes.IsValid)
                {
                    Fail(descRes.Error ?? "Invalid description.");
                    tbCategoryDescription?.Focus();
                    return;
                }
                string? description = string.IsNullOrWhiteSpace(descRes.CleanText) ? name : descRes.CleanText;

                // Duplicate check (NOCASE at DB)
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
                }
                catch (Exception ex)
                {
                    Fail($"Error inserting category: {ex.Message}");
                    return;
                }

                // Success → tell host to flip back & refresh
                ClearError();
                Submitted?.Invoke(this, new CatagorySubmittedEventArgs(name, description));
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            ClearError();
            Canceled?.Invoke(this, EventArgs.Empty);
        }

        /* ---------------- HELPERS ---------------- */

        private void SetBusy(bool isBusy)
        {
            if (btnAddCategory != null) btnAddCategory.IsEnabled = !isBusy;
            if (btnCancel != null) btnCancel.IsEnabled = !isBusy;
        }

        private void ShowError(string message)
        {
            if (txtErrorMessage == null) return;
            txtErrorMessage.Text = message;
            txtErrorMessage.Visibility = Visibility.Visible;
        }

        private void ClearError()
        {
            if (txtErrorMessage == null) return;
            txtErrorMessage.Text = string.Empty;
            txtErrorMessage.Visibility = Visibility.Collapsed;
        }

        private void Fail(string message) => ShowError(message);

        private void FailAndFocus(string message)
        {
            ShowError(message);
            tbCategoryName?.Focus();
            tbCategoryName?.SelectAll();
        }

        /// <summary>
        /// Public helper so host can reset the form after a successful add, if desired.
        /// </summary>
        public void ResetForm()
        {
            tbCategoryName.Text = string.Empty;
            tbCategoryDescription.Text = string.Empty;
            ClearError();
            tbCategoryName.Focus();
        }
    }

    public sealed class CatagorySubmittedEventArgs : EventArgs
    {
        public string Name { get; }
        public string? Description { get; }

        public CatagorySubmittedEventArgs(string name, string? description)
        {
            Name = name;
            Description = string.IsNullOrWhiteSpace(description) ? null : description;
        }
    }
}
