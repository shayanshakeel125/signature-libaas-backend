using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using backend.Data;
using backend.Models;

namespace backend.Controllers
{
    [Route("api/admin/chat-logs")]
    [ApiController]
    public class AdminChatLogsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public AdminChatLogsController(AppDbContext context) => _context = context;

        // GET: api/admin/chat-logs/stats
        [HttpGet("stats")]
        public async Task<ActionResult<ChatStatsResponse>> GetStats()
        {
            var unreadCutoff = DateTime.UtcNow.AddDays(-1);

            var stats = new ChatStatsResponse
            {
                TotalSessions = await _context.ChatSessions.CountAsync(),
                TotalMessages = await _context.ChatMessages.CountAsync(),
                UniqueVisitors = await _context.ChatSessions
                    .Select(s => s.VisitorId).Distinct().CountAsync(),
                Unread = await _context.ChatSessions.CountAsync(s => s.LastMessageAt >= unreadCutoff)
            };

            return Ok(stats);
        }

        // GET: api/admin/chat-logs?page=1&pageSize=20&search=abc&flaggedOnly=true
        [HttpGet]
        public async Task<ActionResult<PagedChatSessionsResponse>> GetSessions([FromQuery] ChatLogQuery query)
        {
            var page = Math.Max(1, query.Page);
            var pageSize = Math.Clamp(query.PageSize, 1, 100);

            var q = _context.ChatSessions.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var term = query.Search.Trim();
                q = q.Where(s => s.VisitorId.Contains(term)
                              || s.Messages.Any(m => m.MessageText.Contains(term)));
            }

            if (query.FlaggedOnly == true)
                q = q.Where(s => s.IsFlagged);

            var totalCount = await q.CountAsync();

            var sessions = await q
                .OrderByDescending(s => s.LastMessageAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Har session ka last message (page ke liye ek hi extra query)
            var ids = sessions.Select(s => s.Id).ToList();
            var lastMessages = await _context.ChatMessages.AsNoTracking()
                .Where(m => ids.Contains(m.ChatSessionId))
                .OrderByDescending(m => m.SentAt).ThenByDescending(m => m.Id)
                .ToListAsync();

            var lastMap = lastMessages
                .GroupBy(m => m.ChatSessionId)
                .ToDictionary(g => g.Key, g => g.First().MessageText);

            return Ok(new PagedChatSessionsResponse
            {
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                Items = sessions.Select(s => new ChatSessionSummaryDto
                {
                    Id = s.Id,
                    VisitorId = s.VisitorId,
                    StartedAt = s.StartedAt,
                    LastMessageAt = s.LastMessageAt,
                    TotalMessages = s.TotalMessages,
                    IsFlagged = s.IsFlagged,
                    LastMessage = lastMap.TryGetValue(s.Id, out var lm) ? lm : null
                }).ToList()
            });
        }

        // GET: api/admin/chat-logs/{id}  (session + saare messages)
        [HttpGet("{id:int}")]
        public async Task<ActionResult<SessionDetailResponse>> GetSession(int id)
        {
            var session = await _context.ChatSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == id);

            if (session == null) return NotFound(new { message = "Session nahi mila." });

            var messages = await _context.ChatMessages.AsNoTracking()
                .Where(m => m.ChatSessionId == id)
                .OrderBy(m => m.SentAt).ThenBy(m => m.Id)
                .Select(m => new ChatMessageDto
                {
                    Id = m.Id,
                    Sender = m.Sender,
                    MessageText = m.MessageText,
                    SentAt = m.SentAt
                })
                .ToListAsync();

            return Ok(new SessionDetailResponse
            {
                Session = new ChatSessionSummaryDto
                {
                    Id = session.Id,
                    VisitorId = session.VisitorId,
                    StartedAt = session.StartedAt,
                    LastMessageAt = session.LastMessageAt,
                    TotalMessages = session.TotalMessages,
                    IsFlagged = session.IsFlagged,
                    LastMessage = messages.LastOrDefault()?.MessageText
                },
                Messages = messages
            });
        }

        // POST: api/admin/chat-logs/{id}/flag   { "isFlagged": true }
        [HttpPost("{id:int}/flag")]
        public async Task<ActionResult> FlagSession(int id, FlagSessionRequest request)
        {
            var session = await _context.ChatSessions.FindAsync(id);
            if (session == null) return NotFound(new { message = "Session nahi mila." });

            session.IsFlagged = request.IsFlagged;
            await _context.SaveChangesAsync();
            return Ok(new { sessionId = session.Id, session.IsFlagged });
        }

        // GET: api/admin/chat-logs/export?format=csv|json&from=2026-10-01&to=2026-10-17
        [HttpGet("export")]
        public async Task<IActionResult> Export(
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] string format = "csv")
        {
            var q = _context.ChatSessions.AsNoTracking();

            if (from.HasValue) q = q.Where(s => s.StartedAt >= from.Value.ToUniversalTime());
            if (to.HasValue) q = q.Where(s => s.StartedAt <= to.Value.ToUniversalTime());

            var sessions = await q
                .OrderByDescending(s => s.LastMessageAt)
                .Select(s => new
                {
                    s.Id,
                    s.VisitorId,
                    s.StartedAt,
                    s.LastMessageAt,
                    s.TotalMessages,
                    s.IsFlagged,
                    LastMessage = s.Messages
                        .OrderByDescending(m => m.SentAt)
                        .Select(m => m.MessageText)
                        .FirstOrDefault()
                })
                .ToListAsync();

            if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
                return Ok(sessions);

            // CSV export
            var sb = new StringBuilder();
            sb.AppendLine("SessionId,VisitorId,StartedAt(UTC),LastMessageAt(UTC),TotalMessages,IsFlagged,LastMessage");
            foreach (var s in sessions)
            {
                sb.AppendLine(string.Join(",",
                    s.Id,
                    Csv(s.VisitorId),
                    s.StartedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    s.LastMessageAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    s.TotalMessages,
                    s.IsFlagged,
                    Csv(s.LastMessage ?? "")));
            }

            var fileName = $"chat-logs-{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
            return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", fileName);
        }

        private static string Csv(string value)
            => $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
