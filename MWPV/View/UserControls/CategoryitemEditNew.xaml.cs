using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace MWPV.View.UserControls
{
    public partial class CategoryitemEditNew : UserControl
    {
        // Events raised back to the Panel overlay host
        public event EventHandler? Submitted;
        public event EventHandler? Canceled;

        private int _categoryKey;
        private string _categoryName = string.Empty;
        private bool _isEditMode;

        public CategoryitemEditNew()
        {
            InitializeComponent();

            Loaded += CategoryitemEditNew_Loaded;
            Unloaded += CategoryitemEditNew_Unloaded;
        }

        /* ======================= Lifecycle ======================= */

        private void CategoryitemEditNew_Loaded(object sender, RoutedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[ITEM-EDIT] Loaded");
#endif
            ClearForm();
        }

        private void CategoryitemEditNew_Unloaded(object sender, RoutedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[ITEM-EDIT] Unloaded");
#endif
            WipeSensitiveFields();
        }

        /* ======================= Configuration ======================= */

        /// <summary>
        /// Called by Panel when opening this control for a new item.
        /// </summary>
        public void ConfigureForAdd(int categoryKey, string categoryName)
        {
            _categoryKey = categoryKey;
            _categoryName = categoryName;
            _isEditMode = false;

#if DEBUG
            Debug.WriteLine($"[ITEM-EDIT] ConfigureForAdd: key={categoryKey}, name='{categoryName}'");
#endif

            ClearForm();
        }

        /// <summary>
        /// (Optional future method) Configure for editing an existing item.
        /// </summary>
        public void ConfigureForEdit(int categoryKey, string categoryName, object existingItem)
        {
            _categoryKey = categoryKey;
            _categoryName = categoryName;
            _isEditMode = true;

#if DEBUG
            Debug.WriteLine($"[ITEM-EDIT] ConfigureForEdit: key={categoryKey}, name='{categoryName}'");
#endif

            // TODO: Map existingItem properties to fields
        }

        /* ======================= Button Events ======================= */

        private void btnSubmit_Click(object sender, RoutedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[ITEM-EDIT] Submit clicked");
#endif

            // Validate basic inputs
            if (string.IsNullOrWhiteSpace(txtItemName.Text))
            {
                MessageBox.Show("Item name is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // TODO: Persist logic here (insert or update DB entry)
#if DEBUG
                Debug.WriteLine($"[ITEM-EDIT] Saving item for categoryKey={_categoryKey}");
#endif

                // Notify parent
                Submitted?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine($"[ITEM-EDIT][ERR] {ex}");
#endif
                MessageBox.Show("Error saving item.\n\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[ITEM-EDIT] Cancel clicked");
#endif
            WipeSensitiveFields();
            Canceled?.Invoke(this, EventArgs.Empty);
        }

        /* ======================= Helpers ======================= */

        private void ClearForm()
        {
            txtItemName.Text = string.Empty;
            txtUrl.Text = string.Empty;
            pwdPassword.Password = string.Empty;
            txtUsername.Text = string.Empty;
            txtEmail.Text = string.Empty;
            txtPhone.Text = string.Empty;
            txtSecQuestion.Text = string.Empty;
            txtSecAnswer.Text = string.Empty;
            txtDescription.Text = string.Empty;
            txtExpDate.Text = string.Empty;
            txtCvv.Text = string.Empty;
            txtAccountNumber.Text = string.Empty;
            txtPin.Text = string.Empty;

            cboItemType.SelectedIndex = -1;
            cboCardType.SelectedIndex = -1;
            cboAccountName.SelectedIndex = -1;
        }

        /// <summary>
        /// Securely wipes all sensitive text fields and passwords.
        /// </summary>
        private void WipeSensitiveFields()
        {
            try
            {
                pwdPassword.Password = string.Empty;
                txtCvv.Text = string.Empty;
                txtPin.Text = string.Empty;
            }
            catch { /* ignore */ }
        }
    }
}
