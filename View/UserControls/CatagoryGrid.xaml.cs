using MWPV.Models;
using MWPV.Services;
using System.Collections.ObjectModel;
using System.Windows;

namespace MWPV.View.UserControls
{
    /// <summary>
    /// Interaction logic for CategoryGrid.xaml
    /// </summary>
    public partial class CategoryGrid : System.Windows.Controls.UserControl

    {
        public event EventHandler CategoryItemClicked;
        // ObservableCollection to hold the categories 20:59 added by the user
        public ObservableCollection<Catagories> BoundCatagories { get; set; } = new ObservableCollection<Catagories>(); 
        public CategoryGrid()
        {
            InitializeComponent();
            // added by the user 20:59
            this.DataContext = this; // Important: bind the DataContext 
        }
        //added by the user 20:59
        public void RefreshCategoryGrid()
        {
            BoundCatagories.Clear();
            var catagories = CategoryService.LoadCatagories();
            foreach (var cat in catagories)
            {
                BoundCatagories.Add(cat);
            }
        }

        //public void RefreshCategoryGrid()
        // {
        //     var BoundCatagories = CategoryService.LoadCatagories();
        //     this.CategoryDataGrid.ItemsSource = BoundCatagories;
        // }

        private void Button1_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is Catagories data)
            {
                //System.Windows.MessageBox.Show($"Button 1 clicked: {data.strCategory1}");
                CategoryItemClicked?.Invoke(this, EventArgs.Empty);
            }
        }
        private void Button2_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is Catagories data)
            {
                //System.Windows.MessageBox.Show($"Button 2 clicked: {data.strCategory2}");
                CategoryItemClicked?.Invoke(this, EventArgs.Empty);
            }
        }
        private void Button3_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is Catagories data)
            {
                //System.Windows.MessageBox.Show($"Button 3 clicked: {data.strCategory3}");
                CategoryItemClicked?.Invoke(this, EventArgs.Empty);
            }
        }
        


    }
}
