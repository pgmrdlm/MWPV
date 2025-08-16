using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace MWPV.View.UserControls
{
    /// <summary>
    /// Interaction logic for Panel.xaml
    /// </summary>
    public partial class Panel : System.Windows.Controls.UserControl
    {
        public Panel()
        {
            InitializeComponent();
            CategoryGrid.CategoryItemClicked += CategoryGrid_CategoryItemClicked;
            CategoryGrid.RefreshCategoryGrid();  // <-- REQUIRED TO LOAD DATA
        }
        public void RefreshCategoryGrid()
        {
            CategoryGrid.RefreshCategoryGrid(); // calls the Refresh method in your CategoryGrid user control
        }
        private void CategoryGrid_CategoryItemClicked(object sender, EventArgs e)
        {
            btnAddCategoryItem.Visibility = Visibility.Visible;
        }
        private void btnAddCategory_Click(object sender, RoutedEventArgs e)
        {
            var mainWindow = Window.GetWindow(this) as MainWindow;
            if (mainWindow != null)
            {
                var categoryWindow = new NewCategoryEntry(mainWindow);
                categoryWindow.Owner = mainWindow;

                bool? result = categoryWindow.ShowDialog(); // Modal window

                if (result == true)
                {
                    // Refresh CatagoryGrid after a category is added
                    RefreshCategoryGrid();
                }
            }
            // Create and show the category entry window
            //var entryWindow = new NewCategoryEntry();




        }
    }

}
