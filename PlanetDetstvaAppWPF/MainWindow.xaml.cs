using System.Windows;

namespace PlanetDetstvaAppWPF
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            lblUser.Text = $"Добро пожаловать, {UserSession.CurrentUserFullName}";
        }

        private void BtnProducts_Click(object sender, RoutedEventArgs e)
        {
            ProductsWindow win = new ProductsWindow();
            win.Owner = this;
            win.ShowDialog();
        }

        private void BtnOrders_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Список заказов (будет в Задании 13)");
        }

        private void BtnLogout_Click(object sender, RoutedEventArgs e)
        {
            UserSession.IsGuest = false;
            LoginWindow login = new LoginWindow();
            login.Show();
            this.Close();
        }
    }
}