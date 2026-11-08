namespace backend.Models
{
    public class ChatMessage
    {
        public int Id { get; set; }

        public int ChatSessionId { get; set; }
        public ChatSession? ChatSession { get; set; }   // Navigation property

        public string Sender { get; set; } = string.Empty;   // "user" | "bot"
        public string MessageText { get; set; } = string.Empty;
        public DateTime SentAt { get; set; }
    }
}
