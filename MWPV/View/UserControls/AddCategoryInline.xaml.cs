// File: MWPV/View/UserControls/AddCategoryInline.xaml.cs
//
// FULL REWRITE
// - Remove UI-side JSON logging (CategoryService now logs via TemplateLogWriter best-effort).
// - Keep validation + duplicate check + insert + Submitted event behavior unchanged.

using System;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MWPV.Services;               // CategoryService
using Security.Utility;            // InputGuards

namespace MWPV.View.UserControls
{
    public enum CategoryFormMode
    {
        Add = 0,
        Edit = 1
    }

    public partial class AddCategoryInline : UserControl
    {
        public event EventHandler<CategorySubmittedEventArgs>? Submitted;
        public event EventHandler<CategoryDeactivatedEventArgs>? Deactivated;
        public event EventHandler? Canceled;

        // Single place to control max length for the name
        private const int MinCategoryNameLength = 4;
        private const int MaxCategoryNameLength = 64; // was 17
        private CategoryFormMode _mode = CategoryFormMode.Add;
        private int _editingCategoryKey;

        public CategoryFormMode Mode => _mode;
        public int EditingCategoryKey => _editingCategoryKey;

        public AddCategoryInline()
        {
            InitializeComponent();
            Loaded += AddCategoryInline_Loaded;
        }

        private void AddCategoryInline_Loaded(object? sender, RoutedEventArgs e)
        {
            // Just focus the name; no category type combo to load anymore
            tbCategoryName?.Focus();
        }

        private void tbCategoryName_TextChanged(object sender, TextChangedEventArgs e) => ClearError();
        private void tbCategoryDescription_TextChanged(object sender, TextChangedEventArgs e) => ClearError();

        private void btnAddCategory_Click(object sender, RoutedEventArgs e)
        {
            ClearError();
            SetBusy(true);

            try
            {
                // --- Validate name (max length -> 64)
                var nameRes = InputGuards.ValidateCategoryName(
                    tbCategoryName?.Text,
                    minLen: MinCategoryNameLength,
                    maxLen: MaxCategoryNameLength
                );

                if (!nameRes.IsValid)
                {
                    FailAndFocus(nameRes.Error ?? $"Category name must be {MaxCategoryNameLength} characters or fewer.");
                    return;
                }
                var name = nameRes.CleanName!;

                // --- Validate description
                var descRes = InputGuards.ValidateDescription(tbCategoryDescription?.Text, maxLen: 512);
                if (!descRes.IsValid)
                {
                    Fail(descRes.Error ?? "Invalid description.");
                    tbCategoryDescription?.Focus();
                    return;
                }

                // If description is empty/whitespace, fall back to name for now
                var description = string.IsNullOrWhiteSpace(descRes.CleanText)
                    ? name
                    : descRes.CleanText;

                // --- Duplicate check
                bool exists;
                try
                {
                    exists = _mode == CategoryFormMode.Edit
                        ? CategoryService.DoesCategoryExistExceptKey(name, _editingCategoryKey)
                        : CategoryService.DoesCategoryExist(name);
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

                // --- Save
                try
                {
                    if (_mode == CategoryFormMode.Edit)
                    {
                        int affected = CategoryService.UpdateCategory(_editingCategoryKey, name, description);
                        if (affected <= 0)
                        {
                            Fail("Category was not updated. It may have already been changed or deactivated.");
                            return;
                        }
                    }
                    else
                    {
                        // New world: Category_Type defaults to 0 in the DB; no type code.
                        // CategoryService is responsible for best-effort template logging.
                        CategoryService.InsertCategory(name, description);
                    }
                }
                catch (Exception ex)
                {
                    Fail(_mode == CategoryFormMode.Edit
                        ? $"Error updating category: {ex.Message}"
                        : $"Error inserting category: {ex.Message}");
                    return;
                }

                // --- Notify host
                Submitted?.Invoke(this, new CategorySubmittedEventArgs(name, description, _editingCategoryKey, _mode));
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

        // -------- UI helpers --------

        private void SetBusy(bool isBusy)
        {
            if (btnAddCategory != null) btnAddCategory.IsEnabled = !isBusy;
            if (btnCancel != null) btnCancel.IsEnabled = !isBusy;
            if (btnDeactivateCategory != null) btnDeactivateCategory.IsEnabled = !isBusy;
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

        /// <summary>Host can call this after a successful add or cancel.</summary>
        public void ResetForm()
        {
            if (tbCategoryName != null) tbCategoryName.Text = string.Empty;
            if (tbCategoryDescription != null) tbCategoryDescription.Text = string.Empty;
            ClearError();
            tbCategoryName?.Focus();
        }

        public void ConfigureForAdd()
        {
            _mode = CategoryFormMode.Add;
            _editingCategoryKey = 0;

            if (txtHeader != null) txtHeader.Text = "Add New Category";
            if (txtSubmitCaption != null) txtSubmitCaption.Text = "Submit";
            if (btnDeactivateCategory != null) btnDeactivateCategory.Visibility = Visibility.Collapsed;

            ResetForm();
        }

        public void ConfigureForEdit(CategoryService.CategoryDetail detail)
        {
            if (detail == null) throw new ArgumentNullException(nameof(detail));

            _mode = CategoryFormMode.Edit;
            _editingCategoryKey = detail.CategoryKey;

            if (txtHeader != null) txtHeader.Text = "Edit Category";
            if (txtSubmitCaption != null) txtSubmitCaption.Text = "Save";
            if (btnDeactivateCategory != null) btnDeactivateCategory.Visibility = Visibility.Visible;

            if (tbCategoryName != null) tbCategoryName.Text = detail.Name ?? string.Empty;
            if (tbCategoryDescription != null) tbCategoryDescription.Text = detail.Description ?? string.Empty;

            ClearError();
            tbCategoryName?.Focus();
            tbCategoryName?.SelectAll();
        }

        private void btnDeactivateCategory_Click(object sender, RoutedEventArgs e)
        {
            if (_mode != CategoryFormMode.Edit || _editingCategoryKey <= 0)
                return;

            ClearError();
            SetBusy(true);

            try
            {
                string categoryName = tbCategoryName?.Text ?? string.Empty;
                int affected = CategoryService.DeactivateCategory(_editingCategoryKey);
                if (affected <= 0)
                {
                    Fail("Category was not deactivated. It may already be inactive.");
                    return;
                }

                Deactivated?.Invoke(this, new CategoryDeactivatedEventArgs(_editingCategoryKey, categoryName));
            }
            catch (Exception ex)
            {
                Fail($"Error deactivating category: {ex.Message}");
            }
            finally
            {
                SetBusy(false);
            }
        }

        // -------- JSON helpers (kept; harmless and used nowhere else) --------
    }

    public sealed class CategorySubmittedEventArgs : EventArgs
    {
        public string Name { get; }
        public string? Description { get; }
        public int CategoryKey { get; }
        public CategoryFormMode Mode { get; }

        public CategorySubmittedEventArgs(
            string name,
            string? description,
            int categoryKey = 0,
            CategoryFormMode mode = CategoryFormMode.Add)
        {
            Name = name;
            Description = string.IsNullOrWhiteSpace(description) ? null : description;
            CategoryKey = categoryKey;
            Mode = mode;
        }
    }

    public sealed class CategoryDeactivatedEventArgs : EventArgs
    {
        public int CategoryKey { get; }
        public string Name { get; }

        public CategoryDeactivatedEventArgs(int categoryKey, string? name)
        {
            CategoryKey = categoryKey;
            Name = name ?? string.Empty;
        }
    }
}
