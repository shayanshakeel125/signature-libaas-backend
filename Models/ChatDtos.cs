using System.ComponentModel.DataAnnotations;

namespace backend.Models
{
    // ---------- Storefront: message save karne ke liye ----------
    public class SaveChatMessageRequest
    {
        [Required, StringLength(64)]
        public string VisitorId { get; set; } = string.Empty;

        [Required, StringLength(20)]
        public string Sender { get; set; } = string.Empty;   // "user" ya "bot"

        [Required, StringLength(4000)]
        public string MessageText { get; set; } = string.Empty;
    }

    public class SaveChatMessageResponse
    {
        public int SessionId { get; set; }
        public int MessageId { get; set; }
        public DateTime SentAt { get; set; }
        public int TotalMessages { get; set; }
    }

    public class ChatHistoryResponse
    {
        public int SessionId { get; set; }
        public string VisitorId { get; set; } = string.Empty;
        public DateTime StartedAt { get; set; }
        public List<ChatMessageDto> Messages { get; set; } = new();
    }

    // ---------- Stats ----------
    public class ChatStatsResponse
    {
        public int TotalSessions { get; set; }
        public int TotalMessages { get; set; }
        public int UniqueVisitors { get; set; }
        public int Unread { get; set; }                 // pichle 24 ghante mein active sessions
        public int MessagesLast24Hours { get; set; }
    }

    // ---------- Paginated logs ----------
    public class ChatLogQuery
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string? Search { get; set; }        // VisitorId ya message text
        public bool? FlaggedOnly { get; set; }
    }

    public class PagedChatSessionsResponse
    {
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public List<ChatSessionSummaryDto> Items { get; set; } = new();
    }

    public class ChatSessionSummaryDto
    {
        public int Id { get; set; }                      // latest session id (View action ke liye)
        public string VisitorId { get; set; } = string.Empty;
        public int SessionCount { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime LastMessageAt { get; set; }
        public int TotalMessages { get; set; }
        public bool IsFlagged { get; set; }
        public string? LastMessage { get; set; }
    }

    public class ChatMessageDto
    {
        public int Id { get; set; }
        public string Sender { get; set; } = string.Empty;
        public string MessageText { get; set; } = string.Empty;
        public DateTime SentAt { get; set; }
    }

    public class SessionDetailResponse
    {
        public ChatSessionSummaryDto Session { get; set; } = new();
        public List<ChatMessageDto> Messages { get; set; } = new();
    }

    public class FlagSessionRequest
    {
        public bool IsFlagged { get; set; }
    }
}
