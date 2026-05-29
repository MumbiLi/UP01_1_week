using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Data.SqlClient;
using System.Configuration;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PlanetDetstvaAppWPF
{
    public partial class LoginWindow : Window
    {
        private int failedAttempts = 0;
        private int remainingBlockSeconds = 0;
        private DispatcherTimer blockTimer;
        private string currentCaptchaText = "";

        public LoginWindow()
        {
            InitializeComponent();
        }

        private void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            string login = txtLogin.Text.Trim();
            string password = txtPassword.Password;

            if (picCaptcha.Visibility == Visibility.Visible && !currentCaptchaText.Equals(txtCaptcha.Text.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                lblError.Text = "Неверный код с картинки!";
                RefreshCaptcha();
                return;
            }

            if (ValidateUser(login, password, out int userId, out string fullName, out int roleId))
            {
                UserSession.CurrentUserId = userId;
                UserSession.CurrentUserFullName = fullName;
                UserSession.CurrentUserRoleId = roleId;
                UserSession.IsGuest = false;
                MainWindow main = new MainWindow();
                main.Show();
                this.Close();
            }
            else
            {
                failedAttempts++;
                if (failedAttempts == 1)
                {
                    lblError.Text = "Неверный логин или пароль!";
                }
                else
                {
                    ShowCaptcha();
                    if (failedAttempts >= 2) StartBlock();
                }
                txtPassword.Clear();
            }
        }

        private void BtnGuest_Click(object sender, RoutedEventArgs e)
        {
            UserSession.IsGuest = true;
            UserSession.CurrentUserFullName = "Гость";
            MainWindow main = new MainWindow();
            main.Show();
            this.Close();
        }

        private bool ValidateUser(string login, string password, out int userId, out string fullName, out int roleId)
        {
            userId = 0;
            fullName = "";
            roleId = 0;

            string cs = ConfigurationManager.ConnectionStrings["PlanetDetstvaDB"].ConnectionString;
            string query = "SELECT UserID, LastName, FirstName, Patronymic, RoleID FROM Users WHERE Login = @Login AND PasswordHash = HASHBYTES('SHA2_256', @Password) AND IsActive = 1";

            using (SqlConnection conn = new SqlConnection(cs))
            using (SqlCommand cmd = new SqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@Login", login);
                cmd.Parameters.AddWithValue("@Password", password);
                conn.Open();
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        userId = reader.GetInt32(0);
                        string ln = reader.GetString(1);
                        string fn = reader.GetString(2);
                        string pt = reader.IsDBNull(3) ? "" : reader.GetString(3);
                        fullName = $"{ln} {fn} {pt}".Trim();
                        roleId = reader.GetInt32(4);
                        return true;
                    }
                }
            }
            return false;
        }

        private void ShowCaptcha()
        {
            picCaptcha.Visibility = Visibility.Visible;
            txtCaptcha.Visibility = Visibility.Visible;
            lblCaptchaMessage.Visibility = Visibility.Visible;
            RefreshCaptcha();
            lblError.Text = "Введите CAPTCHA";
        }

        private void RefreshCaptcha()
        {
            currentCaptchaText = GenerateCaptchaText();
            picCaptcha.Source = DrawCaptcha(currentCaptchaText);
            txtCaptcha.Clear();
        }

        private string GenerateCaptchaText()
        {
            string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz0123456789";
            Random r = new Random();
            return $"{chars[r.Next(chars.Length)]}{chars[r.Next(chars.Length)]}{chars[r.Next(chars.Length)]}{chars[r.Next(chars.Length)]}";
        }

        private BitmapImage DrawCaptcha(string text)
        {
            Bitmap bmp = new Bitmap(250, 70);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.WhiteSmoke);
                Random r = new Random();

                // Шум — линии
                for (int i = 0; i < 30; i++)
                {
                    Pen pen = new Pen(Color.FromArgb(r.Next(100, 200), r.Next(100, 200), r.Next(100, 200)), 1);
                    g.DrawLine(pen, r.Next(250), r.Next(70), r.Next(250), r.Next(70));
                }

                // Рисуем символы (обычный шрифт, без Bold)
                for (int i = 0; i < text.Length; i++)
                {
                    int fontSize = r.Next(24, 38);
                    Font f = new Font("Arial", fontSize);
                    int x = i * 60 + r.Next(5, 25);
                    int y = r.Next(10, 35);
                    g.DrawString(text[i].ToString(), f, Brushes.Black, x, y);
                }

                // Перечёркивание
                g.DrawLine(new Pen(Color.Red, 2), 10, r.Next(20, 50), 240, r.Next(20, 50));
            }

            return ConvertBitmapToBitmapImage(bmp);
        }

        private BitmapImage ConvertBitmapToBitmapImage(Bitmap bitmap)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                bitmap.Save(ms, ImageFormat.Png);
                ms.Position = 0;
                BitmapImage img = new BitmapImage();
                img.BeginInit();
                img.StreamSource = ms;
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                return img;
            }
        }

        private void StartBlock()
        {
            remainingBlockSeconds = 10;
            lblBlockTimer.Text = $"Блокировка: {remainingBlockSeconds} сек";
            lblBlockTimer.Visibility = Visibility.Visible;
            btnLogin.IsEnabled = false;
            btnGuest.IsEnabled = false;
            blockTimer = new DispatcherTimer();
            blockTimer.Interval = TimeSpan.FromSeconds(1);
            blockTimer.Tick += BlockTimer_Tick;
            blockTimer.Start();
        }

        private void BlockTimer_Tick(object sender, EventArgs e)
        {
            remainingBlockSeconds--;
            lblBlockTimer.Text = $"Блокировка: {remainingBlockSeconds} сек";
            if (remainingBlockSeconds <= 0)
            {
                blockTimer.Stop();
                lblBlockTimer.Visibility = Visibility.Collapsed;
                btnLogin.IsEnabled = true;
                btnGuest.IsEnabled = true;
                failedAttempts = 0;
                picCaptcha.Visibility = Visibility.Collapsed;
                txtCaptcha.Visibility = Visibility.Collapsed;
                lblCaptchaMessage.Visibility = Visibility.Collapsed;
                lblError.Text = "";
            }
        }
    }

    public static class UserSession
    {
        public static int CurrentUserId { get; set; }      
        public static string CurrentUserFullName { get; set; }
        public static int CurrentUserRoleId { get; set; }
        public static bool IsGuest { get; set; }
    }
}