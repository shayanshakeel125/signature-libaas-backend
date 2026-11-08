using System;
using System.Collections.Generic;

namespace backend.Models
{
    public class Order
    {
        public int Id { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
        public string PaymentMethod { get; set; } = "COD"; // Cash on Delivery
        public string Status { get; set; } = "Pending";   // Pending, Shipped, Delivered
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Relationship: Ek order mein multiple items ho sakte hain
        public List<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
    }
}