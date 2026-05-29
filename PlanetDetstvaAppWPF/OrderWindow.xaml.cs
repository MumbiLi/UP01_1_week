using System;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Windows;
using iTextSharp.text;
using iTextSharp.text.pdf;

namespace PlanetDetstvaAppWPF
{
    public partial class OrderWindow : Window
    {
        public ObservableCollection<OrderItem> Items { get; set; }

        public OrderWindow()
        {
            InitializeComponent();
            Items = new ObservableCollection<OrderItem>();
            dgvOrder.ItemsSource = Items;
            LoadPickupPoints();
            UpdateTotals();
        }

        public void AddItem(OrderItem item)
        {
            var existing = Items.FirstOrDefault(i => i.ProductID == item.ProductID);
            if (existing != null)
                existing.Quantity += item.Quantity;
            else
                Items.Add(item);
            UpdateTotals();
        }

        private void UpdateTotals()
        {
            decimal total = Items.Sum(i => i.TotalPrice);
            decimal originalTotal = Items.Sum(i => i.Price * i.Quantity);
            decimal discount = originalTotal - total;
            lblTotal.Text = $"{total:F2} ₽";
            lblDiscount.Text = $"{discount:F2} ₽";
        }

        private void RemoveItem_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as System.Windows.Controls.Button;
            int id = (int)btn.Tag;
            var item = Items.FirstOrDefault(i => i.ProductID == id);
            if (item != null)
            {
                Items.Remove(item);
                UpdateTotals();
            }
        }

        private void LoadPickupPoints()
        {
            string cs = ConfigurationManager.ConnectionStrings["PlanetDetstvaDB"].ConnectionString;
            var list = new ObservableCollection<PickupPoint>();
            using (SqlConnection conn = new SqlConnection(cs))
            {
                conn.Open();
                string sql = "SELECT PickupPointID, City, Street, HouseNumber, PostalCode FROM PickupPoints";
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string addr = $"{reader.GetString(1)}, {reader.GetString(2)}, {reader.GetString(3)} (инд.{reader.GetString(4)})";
                        list.Add(new PickupPoint { PickupPointID = reader.GetInt32(0), FullAddress = addr });
                    }
                }
            }
            cmbPickupPoint.ItemsSource = list;
            if (list.Any()) cmbPickupPoint.SelectedIndex = 0;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (cmbPickupPoint.SelectedItem == null)
            {
                MessageBox.Show("Выберите пункт выдачи");
                return;
            }
            if (!Items.Any())
            {
                MessageBox.Show("Корзина пуста");
                return;
            }

            int pickupId = ((PickupPoint)cmbPickupPoint.SelectedItem).PickupPointID;
            int userId = UserSession.CurrentUserId;

            try
            {
                int orderId = SaveOrderToDb(userId, pickupId);
                GeneratePdf(orderId);
                MessageBox.Show($"Заказ оформлен! Код получения: {GetPickupCode(orderId)}");
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}");
            }
        }

        private int SaveOrderToDb(int userId, int pickupPointId)
        {
            string cs = ConfigurationManager.ConnectionStrings["PlanetDetstvaDB"].ConnectionString;
            int orderNumber = 0;
            string pickupCode = "";
            DateTime deliveryDate = DateTime.Now.AddDays(3);

            using (SqlConnection conn = new SqlConnection(cs))
            {
                conn.Open();
                using (SqlTransaction tran = conn.BeginTransaction())
                {
                    try
                    {
                        // номер заказа
                        using (SqlCommand cmd = new SqlCommand("SELECT ISNULL(MAX(OrderNumber),0)+1 FROM Orders", conn, tran))
                            orderNumber = (int)cmd.ExecuteScalar();

                        // код получения
                        Random rnd = new Random();
                        pickupCode = rnd.Next(1000, 9999).ToString();

                        // проверка остатков
                        bool allMore3 = true;
                        foreach (var item in Items)
                        {
                            using (SqlCommand cmd = new SqlCommand("SELECT QuantityInStock FROM Products WHERE ProductID=@id", conn, tran))
                            {
                                cmd.Parameters.AddWithValue("@id", item.ProductID);
                                int stock = (int)cmd.ExecuteScalar();
                                if (stock < 3) allMore3 = false;
                            }
                        }
                        deliveryDate = allMore3 ? DateTime.Now.AddDays(3) : DateTime.Now.AddDays(6);

                        decimal totalAmount = Items.Sum(i => i.TotalPrice);
                        decimal totalDiscount = Items.Sum(i => i.Price * i.Quantity) - totalAmount;

                        // вставка Orders
                        string sqlOrder = @"INSERT INTO Orders (OrderNumber, OrderDate, DeliveryDate, PickupPointID, UserID, PickupCode, StatusID, TotalAmount, TotalDiscount)
                                            VALUES (@num, @date, @delivery, @pickup, @user, @code, 2, @total, @discount);
                                            SELECT SCOPE_IDENTITY();";
                        using (SqlCommand cmd = new SqlCommand(sqlOrder, conn, tran))
                        {
                            cmd.Parameters.AddWithValue("@num", orderNumber);
                            cmd.Parameters.AddWithValue("@date", DateTime.Now);
                            cmd.Parameters.AddWithValue("@delivery", deliveryDate);
                            cmd.Parameters.AddWithValue("@pickup", pickupPointId);
                            cmd.Parameters.AddWithValue("@user", userId);
                            cmd.Parameters.AddWithValue("@code", pickupCode);
                            cmd.Parameters.AddWithValue("@total", totalAmount);
                            cmd.Parameters.AddWithValue("@discount", totalDiscount);
                            int orderId = Convert.ToInt32(cmd.ExecuteScalar());

                            // вставка OrderDetails
                            foreach (var item in Items)
                            {
                                string sqlDetail = @"INSERT INTO OrderDetails (OrderID, ProductID, Quantity, PriceAtMoment, DiscountAtMoment)
                                                     VALUES (@oid, @pid, @qty, @price, @disc)";
                                using (SqlCommand cmd2 = new SqlCommand(sqlDetail, conn, tran))
                                {
                                    cmd2.Parameters.AddWithValue("@oid", orderId);
                                    cmd2.Parameters.AddWithValue("@pid", item.ProductID);
                                    cmd2.Parameters.AddWithValue("@qty", item.Quantity);
                                    cmd2.Parameters.AddWithValue("@price", item.Price);
                                    cmd2.Parameters.AddWithValue("@disc", item.Discount);
                                    cmd2.ExecuteNonQuery();
                                }

                                // списание
                                string sqlUpdate = "UPDATE Products SET QuantityInStock = QuantityInStock - @qty WHERE ProductID = @pid";
                                using (SqlCommand cmd3 = new SqlCommand(sqlUpdate, conn, tran))
                                {
                                    cmd3.Parameters.AddWithValue("@qty", item.Quantity);
                                    cmd3.Parameters.AddWithValue("@pid", item.ProductID);
                                    cmd3.ExecuteNonQuery();
                                }
                            }
                            tran.Commit();
                            return orderId;
                        }
                    }
                    catch
                    {
                        tran.Rollback();
                        throw;
                    }
                }
            }
        }

        private string GetPickupCode(int orderId)
        {
            string cs = ConfigurationManager.ConnectionStrings["PlanetDetstvaDB"].ConnectionString;
            using (SqlConnection conn = new SqlConnection(cs))
            {
                conn.Open();
                using (SqlCommand cmd = new SqlCommand("SELECT PickupCode FROM Orders WHERE OrderID=@id", conn))
                {
                    cmd.Parameters.AddWithValue("@id", orderId);
                    return cmd.ExecuteScalar()?.ToString();
                }
            }
        }

        private void GeneratePdf(int orderId)
        {
            string cs = ConfigurationManager.ConnectionStrings["PlanetDetstvaDB"].ConnectionString;
            OrderInfo info = null;
            using (SqlConnection conn = new SqlConnection(cs))
            {
                conn.Open();
                string sql = @"SELECT o.OrderNumber, o.OrderDate, o.DeliveryDate, o.PickupCode, o.TotalAmount, o.TotalDiscount,
                                      u.LastName, u.FirstName, u.Patronymic,
                                      pp.City, pp.Street, pp.HouseNumber, pp.PostalCode
                               FROM Orders o
                               JOIN Users u ON o.UserID = u.UserID
                               JOIN PickupPoints pp ON o.PickupPointID = pp.PickupPointID
                               WHERE o.OrderID = @id";
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@id", orderId);
                    using (SqlDataReader r = cmd.ExecuteReader())
                    {
                        if (r.Read())
                        {
                            info = new OrderInfo
                            {
                                OrderNumber = r.GetInt32(0),
                                OrderDate = r.GetDateTime(1),
                                DeliveryDate = r.IsDBNull(2) ? (DateTime?)null : r.GetDateTime(2),
                                PickupCode = r.GetString(3),
                                TotalAmount = r.GetDecimal(4),
                                TotalDiscount = r.GetDecimal(5),
                                CustomerName = $"{r.GetString(6)} {r.GetString(7)} {r.GetString(8)}".Trim(),
                                PickupAddress = $"{r.GetString(9)}, {r.GetString(10)}, {r.GetString(11)} (инд.{r.GetString(12)})"
                            };
                        }
                    }
                }
            }

            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"Талон_{info.OrderNumber}.pdf");
            using (Document doc = new Document(PageSize.A4))
            {
                PdfWriter.GetInstance(doc, new FileStream(path, FileMode.Create));
                doc.Open();
                doc.Add(new Paragraph($"ДЕТСКИЙ МАГАЗИН «ПЛАНЕТА ДЕТСТВА»", new Font(Font.FontFamily.HELVETICA, 16, Font.BOLD)) { Alignment = Element.ALIGN_CENTER });
                doc.Add(new Paragraph($"ТАЛОН ЗАКАЗА №{info.OrderNumber}", new Font(Font.FontFamily.HELVETICA, 14, Font.BOLD)) { Alignment = Element.ALIGN_CENTER });
                doc.Add(new Paragraph($"Дата: {info.OrderDate:dd.MM.yyyy HH:mm}"));
                doc.Add(new Paragraph($"Клиент: {info.CustomerName}"));
                doc.Add(new Paragraph($"Пункт выдачи: {info.PickupAddress}"));
                doc.Add(new Paragraph($"Сумма: {info.TotalAmount:F2} ₽"));
                doc.Add(new Paragraph($"Скидка: {info.TotalDiscount:F2} ₽"));
                doc.Add(new Paragraph(" "));
                var codeFont = new Font(Font.FontFamily.HELVETICA, 24, Font.BOLD);
                doc.Add(new Paragraph($"КОД ПОЛУЧЕНИЯ: {info.PickupCode}", codeFont) { Alignment = Element.ALIGN_CENTER });
                doc.Close();
            }
            System.Diagnostics.Process.Start(path);
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private class OrderInfo
        {
            public int OrderNumber { get; set; }
            public DateTime OrderDate { get; set; }
            public DateTime? DeliveryDate { get; set; }
            public string PickupCode { get; set; }
            public decimal TotalAmount { get; set; }
            public decimal TotalDiscount { get; set; }
            public string CustomerName { get; set; }
            public string PickupAddress { get; set; }
        }
    }
}