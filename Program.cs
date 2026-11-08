using Microsoft.EntityFrameworkCore;
using backend.Data;

var builder = WebApplication.CreateBuilder(args);

// 1. Controllers Add Karein
builder.Services.AddTransient<backend.Services.EmailService>();
builder.Services.AddControllers();

// 2. Database Context Register Karein
//    Connection string appsettings.json -> ConnectionStrings:DefaultConnection se aata hai.
//    Environment variable se override karne ke liye:
//    ConnectionStrings__DefaultConnection="Server=...;Database=...;User Id=...;Password=...;"
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// 3. Swagger Services Add Karein
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 3b. HttpClient (Gemini API calls ke liye ChatController mein inject hota hai)
//     Ye line na ho to controller activate nahi hota aur 500 Internal Server Error aata hai.
builder.Services.AddHttpClient();

// 4. CORS: React (Vite dev server) ko allow karein
//    Origins appsettings.json -> Cors:AllowedOrigins se aate hain.
//    Development mein config khali ho to Vite ke default ports auto-allow ho jaate hain.
builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendCors", policy =>
    {
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                             ?? Array.Empty<string>();

        if (builder.Environment.IsDevelopment() && allowedOrigins.Length == 0)
        {
            allowedOrigins = new[]
            {
                "http://localhost:5173",      // Vite default
                "http://localhost:5174",      // Vite jab 5173 busy ho
                "http://127.0.0.1:5173"
            };
        }

        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()               // Authorization, Content-Type, etc.
              .AllowAnyMethod();              // GET, POST, PUT, DELETE, OPTIONS...

        // Agar frontend cookies/session credentials bhejta hai to "Cors:AllowCredentials" = true karein
        if (builder.Configuration.GetValue<bool>("Cors:AllowCredentials"))
            policy.AllowCredentials();
    });
});

var app = builder.Build();

// 5. Development Environment Mein Swagger Enable Karein
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// 6. CORS middleware - MapControllers se pehle, taaki preflight (OPTIONS) requests bhi
//    sahi headers ke saath jawab paayein.
app.UseCors("FrontendCors");

app.UseAuthorization();

// 7. Controllers Route Map Karein
app.MapControllers();

app.Run();