using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Configuration;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PlanetDetstvaAppWPF
{
    public partial class ProductsWindow : Window
    {
        public ProductsWindow()
        {
            InitializeComponent();
            LoadProducts();
        }

        private void LoadProducts()
        {
            string cs = ConfigurationManager.ConnectionStrings["PlanetDetstvaDB"].ConnectionString;
            string sql = @"
                SELECT p.ProductID, p.Article, p.ProductName, p.Price, p.Discount, p.QuantityInStock, p.PhotoPath,
                       s.SupplierName, m.ManufacturerName, c.CategoryName
                FROM Products p
                JOIN Suppliers s ON p.SupplierID = s.SupplierID
                JOIN Manufacturers m ON p.ManufacturerID = m.ManufacturerID
                JOIN Categories c ON p.CategoryID = c.CategoryID
                ORDER BY p.ProductName";

            var list = new ObservableCollection<ProductViewModel>();

            using (SqlConnection conn = new SqlConnection(cs))
            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                conn.Open();
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var p = new ProductViewModel
                        {
                            ProductID = reader.GetInt32(0),
                            Article = reader.GetString(1),
                            ProductName = reader.GetString(2),
                            Price = reader.GetDecimal(3),
                            Discount = reader.GetDecimal(4),
                            QuantityInStock = reader.GetInt32(5),
                            PhotoPath = reader.IsDBNull(6) ? null : reader.GetString(6),
                            SupplierName = reader.GetString(7),
                            ManufacturerName = reader.GetString(8),
                            CategoryName = reader.GetString(9)
                        };
                        list.Add(p);
                    }
                }
            }

            dgvProducts.ItemsSource = list;
            lblCount.Text = $"Всего товаров: {list.Count}";
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private OrderWindow orderWindow;
        private void AddToOrder_Click(object sender, RoutedEventArgs e)
        {
            Button btn = sender as Button;
            if (btn?.Tag != null)
            {
                int productId = (int)btn.Tag;
                var product = ((ObservableCollection<ProductViewModel>)dgvProducts.ItemsSource)
                    .FirstOrDefault(p => p.ProductID == productId);
                if (product != null)
                {
                    var item = new OrderItem
                    {
                        ProductID = product.ProductID,
                        ProductName = product.ProductName,
                        Article = product.Article,
                        Price = product.Price,
                        Discount = product.Discount,
                        Quantity = 1,
                        PhotoPath = product.PhotoPath
                    };
                    if (orderWindow == null)
                        orderWindow = new OrderWindow();
                    orderWindow.AddItem(item);
                    orderWindow.Show();
                    orderWindow.Focus();
                }
            }
        }
    }

    public class ProductViewModel : INotifyPropertyChanged
    {
        public int ProductID { get; set; }
        public string Article { get; set; }
        public string ProductName { get; set; }
        public decimal Price { get; set; }
        public decimal Discount { get; set; }
        public int QuantityInStock { get; set; }
        public string PhotoPath { get; set; }
        public string SupplierName { get; set; }
        public string ManufacturerName { get; set; }
        public string CategoryName { get; set; }

        public string PriceDisplay => $"{Price:F2} ₽";

        public decimal FinalPrice => Price - (Price * Discount / 100);

        public string FinalPriceDisplay => Discount > 0 ? $"{FinalPrice:F2} ₽" : "";

        public SolidColorBrush RowColor
        {
            get
            {
                if (QuantityInStock == 0)
                    return new SolidColorBrush(Color.FromRgb(139, 69, 19)); // коричневый
                if (Discount > 15)
                    return new SolidColorBrush(Color.FromRgb(255, 217, 61)); // #FFD93D
                return new SolidColorBrush(Colors.White);
            }
        }

        public BitmapImage PhotoImage
        {
            get
            {
                string baseDir = @"C:\PlanetDetstva\Images\";
                string placeholder = baseDir + "picture.png";
                string fullPath = string.IsNullOrEmpty(PhotoPath) ? placeholder : baseDir + PhotoPath;

                if (File.Exists(fullPath))
                {
                    var img = new BitmapImage();
                    img.BeginInit();
                    img.UriSource = new Uri(fullPath);
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    img.EndInit();
                    return img;
                }

                if (File.Exists(placeholder))
                {
                    var img = new BitmapImage();
                    img.BeginInit();
                    img.UriSource = new Uri(placeholder);
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    img.EndInit();
                    return img;
                }

                return null;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}