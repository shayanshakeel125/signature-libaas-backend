namespace backend.Models
{
    public class ChatSession
    {
        public int Id { get; set; }
        public string VisitorId { get; set; } = string.Empty;
        public DateTime StartedAt { get; set; }
        public DateTime LastMessageAt { get; set; }
        public int TotalMessages { get; set; }
        public bool IsFlagged { get; set; }

        // One-to-Many: ek session mein bahut se messages
        public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
    }
}
