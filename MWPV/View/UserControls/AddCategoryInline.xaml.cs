using System;
using System.Windows;
using System.Windows.Controls;
using MWPV.Services;          // CategoryService
using Security.Utility;       // InputGuards

namespace MWPV.View.UserControls
{
    public partial class AddCategoryInline : UserControl
    {
        public event EventHandler<CategorySubmittedEventArgs>? Submitted;
        public event EventHandler? Canceled;

        public AddCategoryInline()
        {
            InitializeComponent();
            Loaded += (_, __) => tbCategoryName?.Focus();
        }

        private void tbCategoryName_TextChanged(object sender, TextChangedEventArgs e) => ClearError();
        private void tbCategoryDescription_TextChanged(object sender, TextChangedEventArgs e) => ClearError();

        private void btnAddCategory_Click(object sender, RoutedEventArgs e)
        {
            ClearError();
            SetBusy(true);

            try
            {
                // Validate name (centralized rules)
                var nameRes = InputGuards.ValidateCategoryName(tbCategoryName?.Text, minLen: 4, maxLen: 17);
                if (!nameRes.IsValid)
                {
                    FailAndFocus(nameRes.Error ?? "Invalid category name.");
                    return;
                }
                var name = nameRes.CleanName!;

                // Validate description
                var descRes = InputGuards.ValidateDescription(tbCategoryDescription?.Text, maxLen: 512);
                if (!descRes.IsValid)
                {
                    Fail(descRes.Error ?? "Invalid description.");
                    tbCategoryDescription?.Focus();
                    return;
                }
                var description = string.IsNullOrWhiteSpace(descRes.CleanText) ? name : descRes.CleanText;

                // Duplicate check (uses corrected API name)
                bool exists;
                try
                {
                    exists = CategoryService.DoesCategoryExist(name);
                }
                catch (Exception ex)
                {
                    FailAndFocus($"Error checking duplicates: {ex.Message}");
                    return;
                }

                if (exists)
                {
                    FailAndFocus("Category already exists. Please enter a different name.");
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

                // Success
                Submitted?.Invoke(this, new CategorySubmittedEventArgs(name, description));
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

        // Host can call this after a successful add or cancel
        public void ResetForm()
        {
            tbCategoryName.Text = string.Empty;
            tbCategoryDescription.Text = string.Empty;
            ClearError();
            tbCategoryName.Focus();
        }
    }

    public sealed class CategorySubmittedEventArgs : EventArgs
    {
        public string Name { get; }
        public string? Description { get; }

        public CategorySubmittedEventArgs(string name, string? description)
        {
            Name = name;
            Description = string.IsNullOrWhiteSpace(description) ? null : description;
        }
    }
}
