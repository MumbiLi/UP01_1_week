using System.Collections.ObjectModel;

namespace PlanetDetstvaAppWPF
{
    public class OrderItem
    {
        public int ProductID { get; set; }
        public string ProductName { get; set; }
        public string Article { get; set; }
        public decimal Price { get; set; }
        public decimal Discount { get; set; }
        public int Quantity { get; set; }
        public string PhotoPath { get; set; }

        public decimal FinalPrice => Price - (Price * Discount / 100);
        public decimal TotalPrice => FinalPrice * Quantity;
    }

    public class PickupPoint
    {
        public int PickupPointID { get; set; }
        public string FullAddress { get; set; }
    }
}
