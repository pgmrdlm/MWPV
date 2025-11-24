// File: MWPV/View/UserControls/AddCategoryInline.xaml.cs
using System;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MWPV.Services;               // CategoryService
using Security.Utility;            // InputGuards
using Utilities.Helpers;           // ErrorHandler
using MWPV.Utilities.Json;         // AppJson

namespace MWPV.View.UserControls
{
    public partial class AddCategoryInline : UserControl
    {
        public event EventHandler<CategorySubmittedEventArgs>? Submitted;
        public event EventHandler? Canceled;

        // Single place to control max length for the name
        private const int MinCategoryNameLength = 4;
        private const int MaxCategoryNameLength = 64; // was 17

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

        private async void btnAddCategory_Click(object sender, RoutedEventArgs e)
        {
            ClearError();
            SetBusy(true);

            try
            {
                // --- Validate name (only change: max length -> 64)
                var nameRes = InputGuards.ValidateCategoryName(
                    tbCategoryName?.Text,
                    minLen: MinCategoryNameLength,
                    maxLen: MaxCategoryNameLength
                );

                if (!nameRes.IsValid)
                {
                    // If guard didn’t provide a message, show one that reflects the new max
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

                // --- Insert
                try
                {
                    // New world: Category_Type defaults to 0 in the DB; no type code.
                    CategoryService.InsertCategory(name, description);
                }
                catch (Exception ex)
                {
                    Fail($"Error inserting category: {ex.Message}");
                    return;
                }

                // --- Structured log (fire-and-forget)
                try
                {
                    var payload = new AppJson.LogPayloadDto
                    {
                        Message = "Category inserted",
                        Source = "AddCategoryInline",
                        EventCode = "CATEGORY_INSERTED",
                        OccurredUtc = DateTime.UtcNow,
                        Context = BuildContext(new { name, description })
                    };

                    var json = AppJson.SerializeLogPayload(payload, pretty: false);
                    ErrorHandler.Info("CategoryService", json);
                }
                catch
                {
                    // Never let logging side-effects break UX
                }

                // --- Notify host
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

        // -------- UI helpers --------

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

        /// <summary>Host can call this after a successful add or cancel.</summary>
        public void ResetForm()
        {
            if (tbCategoryName != null) tbCategoryName.Text = string.Empty;
            if (tbCategoryDescription != null) tbCategoryDescription.Text = string.Empty;
            ClearError();
            tbCategoryName?.Focus();
        }

        // -------- JSON helpers --------

        /// <summary>
        /// Wraps an anonymous object as a JsonElement so it fits AppJson.LogPayloadDto.Context.
        /// </summary>
        private static JsonElement? BuildContext(object obj)
        {
            try
            {
                var json = AppJson.Serialize(obj, pretty: false);
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.Clone();
            }
            catch
            {
                return null;
            }
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
