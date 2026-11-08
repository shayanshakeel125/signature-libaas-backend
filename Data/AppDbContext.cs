using Microsoft.EntityFrameworkCore;
using backend.Models;

namespace backend.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Product> Products { get; set; }
        public DbSet<User> Users { get; set; }

        // --- AI Chatbot History & Logs ---
        public DbSet<ChatSession> ChatSessions { get; set; }
        public DbSet<ChatMessage> ChatMessages { get; set; }

        // --- Orders ---
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ChatSession -> ChatMessage One-to-Many
            modelBuilder.Entity<ChatSession>(entity =>
            {
                entity.ToTable("ChatSessions");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.VisitorId).HasMaxLength(64).IsRequired();
                entity.HasIndex(x => x.VisitorId);
                entity.HasIndex(x => x.LastMessageAt);

                entity.HasMany(x => x.Messages)
                      .WithOne(x => x.ChatSession)
                      .HasForeignKey(x => x.ChatSessionId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ChatMessage>(entity =>
            {
                entity.ToTable("ChatMessages");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Sender).HasMaxLength(20).IsRequired();
                entity.Property(x => x.MessageText).IsRequired();
                entity.HasIndex(x => x.ChatSessionId);
            });

            // Order -> OrderItem One-to-Many Configuration
            modelBuilder.Entity<Order>(entity =>
            {
                entity.ToTable("Orders");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.TotalAmount).HasColumnType("decimal(18,2)");

                entity.HasMany(x => x.OrderItems)
                      .WithOne() // 🟢 Yahan se (x => x.Order) hata diya
                      .HasForeignKey(x => x.OrderId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<OrderItem>(entity =>
            {
                entity.ToTable("OrderItems");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Price).HasColumnType("decimal(18,2)");
            });
        }
    }
}