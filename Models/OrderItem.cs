namespace backend.Models
{
    public class OrderItem
    {
        public int Id { get; set; }
        
        // Foreign Key to Order
        public int OrderId { get; set; }

        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string SelectedSize { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal Price { get; set; }
    }
}