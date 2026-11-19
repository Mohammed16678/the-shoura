using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Shoura.API.Security;
using Shoura.API.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();

// SQLite للتطوير المحلي، وPostgreSQL للنشر متعدد النسخ.
var databaseProvider = builder.Configuration["Database:Provider"] ?? Environment.GetEnvironmentVariable("SHOURA_DATABASE_PROVIDER") ?? "Sqlite";
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=shoura.db";
if (databaseProvider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
    connectionString = Environment.GetEnvironmentVariable("SHOURA_CONNECTION_STRING") ?? builder.Configuration.GetConnectionString("Postgres") ?? throw new InvalidOperationException("Set SHOURA_CONNECTION_STRING or ConnectionStrings:Postgres.");
builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (databaseProvider.Equals("Postgres", StringComparison.OrdinalIgnoreCase)) options.UseNpgsql(connectionString);
    else if (databaseProvider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase)) options.UseSqlite(connectionString);
    else throw new InvalidOperationException("Database:Provider must be Sqlite or Postgres.");
});

builder.Services.AddControllers();
builder.Services.AddHttpsRedirection(options =>
{
    var configuredPort = builder.Configuration.GetValue<int?>("HttpsRedirection:HttpsPort");
    if (configuredPort.HasValue) options.HttpsPort = configuredPort.Value;
    else if (!builder.Environment.IsDevelopment()) options.HttpsPort = 443;
});
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
if (allowedOrigins.Length > 0)
{
    builder.Services.AddCors(options => options.AddPolicy("configured-origins", policy =>
        policy.WithOrigins(allowedOrigins).WithMethods("GET", "POST", "DELETE", "OPTIONS").WithHeaders("Authorization", "Content-Type")));
}
builder.Services.AddHttpClient("Gemini", client => client.Timeout = TimeSpan.FromMinutes(3));
var keyRingPath = builder.Configuration["DataProtection:KeyRingPath"] ?? Environment.GetEnvironmentVariable("SHOURA_DATA_PROTECTION_PATH");
if (string.IsNullOrWhiteSpace(keyRingPath) && builder.Environment.IsDevelopment())
    keyRingPath = Path.Combine(Path.GetTempPath(), "shoura-data-protection");
if (!string.IsNullOrWhiteSpace(keyRingPath))
{
    Directory.CreateDirectory(keyRingPath);
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath))
        .SetApplicationName("Shoura");
}
var configuredSigningKey = builder.Configuration["Authentication:SigningKey"] ?? Environment.GetEnvironmentVariable("SHOURA_AUTH_SIGNING_KEY");
if (string.IsNullOrWhiteSpace(configuredSigningKey) && !builder.Environment.IsDevelopment())
    throw new InvalidOperationException("Set SHOURA_AUTH_SIGNING_KEY to a random secret of at least 32 characters before running outside Development.");
var signingKey = string.IsNullOrWhiteSpace(configuredSigningKey)
    ? RandomNumberGenerator.GetBytes(32)
    : Encoding.UTF8.GetBytes(configuredSigningKey);
if (signingKey.Length < 32) throw new InvalidOperationException("The authentication signing key must be at least 32 bytes.");
builder.Services.AddSingleton(new BearerTokenService(signingKey));
builder.Services.AddAuthentication("Bearer").AddScheme<AuthenticationSchemeOptions, BearerTokenHandler>("Bearer", _ => { });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.OnRejected = (context, _) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        return ValueTask.CompletedTask;
    };
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        context.Request.Path.StartsWithSegments("/api")
            ? RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 120,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                })
            : RateLimitPartition.GetNoLimiter("non-api"));
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("ai", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("uploads", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 3,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

if (databaseProvider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase) && !OperatingSystem.IsWindows())
{
    var databaseFile = new SqliteConnectionStringBuilder(connectionString).DataSource;
    if (!string.IsNullOrWhiteSpace(databaseFile) && databaseFile != ":memory:")
    {
        var fullDatabaseFile = Path.GetFullPath(databaseFile);
        foreach (var path in new[] { fullDatabaseFile, fullDatabaseFile + "-wal", fullDatabaseFile + "-shm" })
            if (File.Exists(path)) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}

var configuredMediaPath = builder.Configuration["Media:StoragePath"] ?? Environment.GetEnvironmentVariable("SHOURA_MEDIA_PATH");
var mediaPath = string.IsNullOrWhiteSpace(configuredMediaPath)
    ? Path.Combine(builder.Environment.ContentRootPath, "uploads")
    : Path.GetFullPath(configuredMediaPath);
Directory.CreateDirectory(mediaPath);
if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(mediaPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
List<string> retainedMediaNames;
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var messageMediaUrls = await db.Messages.AsNoTracking().Where(m => m.MediaUrl != null).Select(m => m.MediaUrl!).ToListAsync();
    retainedMediaNames = messageMediaUrls
        .Select(url => MediaReference.TryGetStoredFileName(url, out var name) ? name : null)
        .Where(name => name is not null)
        .Select(name => name!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
}
foreach (var storedPath in Directory.EnumerateFiles(mediaPath))
{
    var fileName = Path.GetFileName(storedPath);
    if (MediaReference.TryGetStoredFileName($"/api/media/{fileName}", out _) &&
        !retainedMediaNames.Contains(fileName, StringComparer.OrdinalIgnoreCase))
        File.Delete(storedPath);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
    context.Response.Headers.TryAdd("X-Frame-Options", "DENY");
    context.Response.Headers.TryAdd("Referrer-Policy", "no-referrer");
    context.Response.Headers.TryAdd("Permissions-Policy", "camera=(), geolocation=(), microphone=()");
    context.Response.Headers.TryAdd("Content-Security-Policy", "default-src 'self'; img-src 'self' data: https:; media-src 'self' https:; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'");
    if (context.Request.Path.StartsWithSegments("/api"))
        context.Response.Headers.CacheControl = "no-store";
    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRouting();
if (allowedOrigins.Length > 0) app.UseCors("configured-origins");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// ربط الـ Controllers الخاصة بمشروعنا
app.MapControllers();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

app.Run();
