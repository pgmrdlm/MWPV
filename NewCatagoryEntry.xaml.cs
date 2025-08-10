using MWPV.Services;
//using System.Data.Entity;
using System.Windows;
using System.Windows.Controls;

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

        private void btnAddCategory_Click(object sender, RoutedEventArgs e)
        {
            if (tbCategoryName.Text.Length > 3)
            {
                string newCategoryName = tbCategoryName.Text;
                bool bolExists = CategoryService.DoesCatagoryExist(newCategoryName);
                if (bolExists)
                {
                    txtErrorMessage.Text = "Category already exists. Please enter a different name.";
                    txtErrorMessage.Visibility = Visibility.Visible;
                    return;
                }
                else
                {
                    CategoryService.InsertCategory(newCategoryName);
                    _mainWindow.Panel.RefreshCategoryGrid();
                    this.DialogResult = true;
                    this.Close();
                }
            }
            else
            {
                txtErrorMessage.Text = "Category name must be at least 4 characters.";
                txtErrorMessage.Visibility = Visibility.Visible;
            }
        }
    }
}