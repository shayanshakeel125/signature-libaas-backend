using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using backend.Data;
using backend.Models;

namespace backend.Controllers
{
    [Route("api/chat")]
    [ApiController]
    public class ChatController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger<ChatController> _logger;
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;

        // 30 min inactivity ke baad nayi session banegi (purani session continue hogi)
        private static readonly TimeSpan SessionIdleTimeout = TimeSpan.FromMinutes(30);
        private static readonly string[] AllowedSenders = { "user", "bot" };

        // Signature AI ka official system prompt (sirf Signature Libaas ke liye)
        private const string systemPrompt = @"You are Signature AI, the official customer support assistant ONLY for the 'Signature Libaas' online clothing store.

STRICT BOUNDARIES:
1. ONLY TALK ABOUT SIGNATURE LIBAAS: Answer questions exclusively related to Signature Libaas store products (T-Shirts, Kurtis, Hoodies, Casual & Formal Wear), orders, shipping, and returns.
2. REFUSE OUT-OF-TOPIC QUERIES: If a user asks about anything unrelated to Signature Libaas or general non-shopping questions, politely respond: 'Main sirf Signature Libaas store aur hamari products ke baare mein madad kar sakta hoon.'
3. NO GREETING CONFIRMATION: Answer the customer's question directly without repeating system instructions or intro greetings.
4. SPEED & LENGTH: Keep every answer under 2-3 short sentences (maximum 30 words) for instant responses.
5. LANGUAGE: Match the user's language (Roman Urdu, English, or Urdu).";

        public ChatController(
            AppDbContext context, 
            ILogger<ChatController> logger, 
            IConfiguration configuration, 
            HttpClient httpClient)
        {
            _context = context;
            _logger = logger;
            _configuration = configuration;
            _httpClient = httpClient;
        }

        // ============================================================
        // STOREFRONT ENDPOINTS
        // ============================================================
        // POST: api/chat  (Gemini AI response ke liye)
        [HttpPost]
        public async Task<IActionResult> PostChat([FromBody] ChatRequest request, CancellationToken cancellationToken)
        {
            var messageText = request?.Message?.Trim();
            if (string.IsNullOrWhiteSpace(messageText))
            {
                return BadRequest(new { error = "Message is required." });
            }

            string apiKey = _configuration["Gemini:ApiKey"];
            if (string.IsNullOrEmpty(apiKey))
            {
                return StatusCode(500, new { error = "Gemini API key is not configured on the server." });
            }

            // Note: gemini-1.5-flash retired ho chuka hai (404 model not found deta hai).
            // Naye projects ke liye recommended: gemini-3.5-flash-lite ya gemini-3.8-flash
            string url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-3.5-flash-lite:generateContent?key={apiKey}";

            // System prompt + user message (prompt ab alag systemInstruction mein bheja jaata hai)
            var payload = new
            {
                systemInstruction = new
                {
                    parts = new[]
                    {
                        new { text = systemPrompt }
                    }
                },
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = messageText }
                        }
                    }
                }
            };

            var content = new StringContent(System.Text.Json.JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync(url, content, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    // Gemini ka asal error body log karo, warna debugging andha ho jaati hai
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError("Gemini API call failed. Status: {StatusCode}, Body: {Body}",
                        (int)response.StatusCode, errorBody);
                    return StatusCode(500, new { error = "Failed to communicate with Gemini API." });
                }

                var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = System.Text.Json.JsonDocument.Parse(responseString);

                var reply = doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString() ?? "Maaf kijiye, main abhi jawab nahi de pa raha.";

                return Ok(new { reply = reply.Trim() });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gemini API error occurred.");
                return StatusCode(500, new { error = $"Server error: {ex.Message}" });
            }
        }

        // POST: api/chat/messages  (storefront se message save)
        [HttpPost("messages")]
        public async Task<ActionResult<SaveChatMessageResponse>> SaveMessage(
            SaveChatMessageRequest request,
            CancellationToken cancellationToken)
        {
            var sender = request.Sender.Trim().ToLowerInvariant();
            if (!AllowedSenders.Contains(sender))
                return BadRequest(new { message = "Sender sirf 'user' ya 'bot' ho sakta hai." });

            var visitorId = request.VisitorId.Trim();
            if (visitorId.Length == 0)
                return BadRequest(new { message = "VisitorId khali nahi ho sakta." });

            var messageText = request.MessageText.Trim();
            if (messageText.Length == 0)
                return BadRequest(new { message = "MessageText khali nahi ho sakta." });

            try
            {
                var now = DateTime.UtcNow;

                // Visitor ki latest session dhoondho (30 min idle timeout rule)
                var session = await _context.ChatSessions
                    .Where(s => s.VisitorId == visitorId)
                    .OrderByDescending(s => s.StartedAt)
                    .FirstOrDefaultAsync(cancellationToken);

                if (session == null || now - session.LastMessageAt > SessionIdleTimeout)
                {
                    session = new ChatSession
                    {
                        VisitorId = visitorId,
                        StartedAt = now,
                        LastMessageAt = now,
                        TotalMessages = 0,
                        IsFlagged = false
                    };
                    _context.ChatSessions.Add(session);
                }

                session.LastMessageAt = now;
                session.TotalMessages++;

                var message = new ChatMessage
                {
                    ChatSession = session,
                    Sender = sender,
                    MessageText = messageText,
                    SentAt = now
                };
                _context.ChatMessages.Add(message);

                await _context.SaveChangesAsync(cancellationToken);   // single save = atomic

                return Ok(new SaveChatMessageResponse
                {
                    SessionId = session.Id,
                    MessageId = message.Id,
                    SentAt = message.SentAt,
                    TotalMessages = session.TotalMessages
                });
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Chat message save nahi ho saka (VisitorId: {VisitorId})", visitorId);
                return Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Message save nahi ho saka.",
                    detail: "Database error. Please try again.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while saving chat message (VisitorId: {VisitorId})", visitorId);
                return Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Message save nahi ho saka.",
                    detail: "Unexpected server error.");
            }
        }

        // GET: api/chat/{visitorId}  (page reload ke baad history wapas)
        [HttpGet("{visitorId}")]
        public async Task<ActionResult<ChatHistoryResponse>> GetHistory(
            string visitorId,
            CancellationToken cancellationToken)
        {
            var session = await _context.ChatSessions
                .AsNoTracking()
                .Where(s => s.VisitorId == visitorId)
                .OrderByDescending(s => s.StartedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (session == null) return NotFound(new { message = "Session nahi mila." });

            var messages = await GetSessionMessagesAsync(session.Id, cancellationToken);

            return Ok(new ChatHistoryResponse
            {
                SessionId = session.Id,
                VisitorId = session.VisitorId,
                StartedAt = session.StartedAt,
                Messages = messages
            });
        }

        // ============================================================
        // ADMIN PANEL ENDPOINTS (chat logs + analytics)
        // ============================================================

        // GET: api/chat/sessions?page=1&pageSize=20&search=abc&flaggedOnly=true
        // VisitorId ke hisaab se group karke sessions/logs bhejta hai.
        [HttpGet("sessions")]
        public async Task<ActionResult<PagedChatSessionsResponse>> GetSessions(
            [FromQuery] ChatLogQuery query,
            CancellationToken cancellationToken)
        {
            var page = Math.Max(1, query.Page);
            var pageSize = Math.Clamp(query.PageSize, 1, 100);

            var sessions = _context.ChatSessions.AsNoTracking();

            var search = query.Search?.Trim();
            if (!string.IsNullOrEmpty(search))
            {
                sessions = sessions.Where(s =>
                    s.VisitorId.Contains(search) ||
                    s.Messages.Any(m => m.MessageText.Contains(search)));
            }

            if (query.FlaggedOnly == true)
                sessions = sessions.Where(s => s.IsFlagged);

            var totalCount = await sessions
                .GroupBy(s => s.VisitorId)
                .CountAsync(cancellationToken);

            var items = await sessions
                .GroupBy(s => s.VisitorId)
                .Select(g => new ChatSessionSummaryDto
                {
                    Id = g.Max(s => s.Id),                       // latest session id (View action ke liye)
                    VisitorId = g.Key,
                    SessionCount = g.Count(),
                    StartedAt = g.Min(s => s.StartedAt),
                    LastMessageAt = g.Max(s => s.LastMessageAt),
                    TotalMessages = g.Sum(s => s.TotalMessages),
                    IsFlagged = g.Any(s => s.IsFlagged),
                    LastMessage = g.SelectMany(s => s.Messages)
                                   .OrderByDescending(m => m.SentAt)
                                   .Select(m => m.MessageText)
                                   .FirstOrDefault()
                })
                .OrderByDescending(x => x.LastMessageAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return Ok(new PagedChatSessionsResponse
            {
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                Items = items
            });
        }

        // GET: api/chat/sessions/{sessionId}  (View action - poori conversation)
        [HttpGet("sessions/{sessionId:int}")]
        public async Task<ActionResult<SessionDetailResponse>> GetSession(
            int sessionId,
            CancellationToken cancellationToken)
        {
            var session = await _context.ChatSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

            if (session == null) return NotFound(new { message = "Session nahi mila." });

            var messages = await GetSessionMessagesAsync(sessionId, cancellationToken);

            return Ok(new SessionDetailResponse
            {
                Session = new ChatSessionSummaryDto
                {
                    Id = session.Id,
                    VisitorId = session.VisitorId,
                    SessionCount = 1,
                    StartedAt = session.StartedAt,
                    LastMessageAt = session.LastMessageAt,
                    TotalMessages = session.TotalMessages,
                    IsFlagged = session.IsFlagged,
                    LastMessage = messages.Count > 0 ? messages[^1].MessageText : null
                },
                Messages = messages
            });
        }

        // PUT: api/chat/sessions/{sessionId}/flag  (Flag action - admin ke liye)
        [HttpPut("sessions/{sessionId:int}/flag")]
        public async Task<IActionResult> FlagSession(
            int sessionId,
            FlagSessionRequest request,
            CancellationToken cancellationToken)
        {
            var session = await _context.ChatSessions
                .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

            if (session == null) return NotFound(new { message = "Session nahi mila." });

            session.IsFlagged = request.IsFlagged;

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Session flag update nahi ho saka (SessionId: {SessionId})", sessionId);
                return Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Flag update nahi ho saka.",
                    detail: "Database error. Please try again.");
            }

            return Ok(new { sessionId = session.Id, isFlagged = session.IsFlagged });
        }

        // GET: api/chat/stats  (admin analytics)
        [HttpGet("stats")]
        public async Task<ActionResult<ChatStatsResponse>> GetStats(CancellationToken cancellationToken)
        {
            var cutoff = DateTime.UtcNow.AddHours(-24);

            var sessionStats = await _context.ChatSessions
                .AsNoTracking()
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    TotalSessions = g.Count(),
                    UniqueVisitors = g.Select(s => s.VisitorId).Distinct().Count(),
                    ActiveLast24h = g.Count(s => s.LastMessageAt >= cutoff)
                })
                .FirstOrDefaultAsync(cancellationToken);

            var totalMessages = await _context.ChatMessages
                .AsNoTracking()
                .CountAsync(cancellationToken);

            var messagesLast24h = await _context.ChatMessages
                .AsNoTracking()
                .CountAsync(m => m.SentAt >= cutoff, cancellationToken);

            return Ok(new ChatStatsResponse
            {
                TotalSessions = sessionStats?.TotalSessions ?? 0,
                TotalMessages = totalMessages,
                UniqueVisitors = sessionStats?.UniqueVisitors ?? 0,
                Unread = sessionStats?.ActiveLast24h ?? 0,
                MessagesLast24Hours = messagesLast24h
            });
        }

        // ============================================================
        // HELPERS
        // ============================================================

        private async Task<List<ChatMessageDto>> GetSessionMessagesAsync(int sessionId, CancellationToken cancellationToken)
        {
            return await _context.ChatMessages
                .AsNoTracking()
                .Where(m => m.ChatSessionId == sessionId)
                .OrderBy(m => m.SentAt).ThenBy(m => m.Id)
                .Select(m => new ChatMessageDto
                {
                    Id = m.Id,
                    Sender = m.Sender,
                    MessageText = m.MessageText,
                    SentAt = m.SentAt
                })
                .ToListAsync(cancellationToken);
        }
    }

    public class ChatRequest
    {
        public string Message { get; set; }
    }
}