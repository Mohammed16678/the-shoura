using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shoura.API.Data;
using Shoura.API.Security;

namespace Shoura.API.Controllers;

[ApiController]
[Authorize]
[Route("api/media")]
public sealed class MediaController(IConfiguration configuration, IWebHostEnvironment environment, AppDbContext db) : ControllerBase
{
    private const long MaxFileSize = 100 * 1024 * 1024;
    private static readonly Dictionary<string, string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image", [".jpeg"] = "image", [".png"] = "image", [".webp"] = "image", [".gif"] = "image",
        [".mp4"] = "video", [".webm"] = "video", [".mov"] = "video",
        [".mp3"] = "audio", [".m4a"] = "audio", [".aac"] = "audio",
        [".wav"] = "audio", [".ogg"] = "audio", [".opus"] = "audio"
    };

    [HttpPost("upload")]
    [EnableRateLimiting("uploads")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxFileSize)]
    public async Task<ActionResult<MediaUploadDto>> Upload([FromForm] IFormFile file, CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0)
            return Unauthorized();
        if (file is null || file.Length == 0) return BadRequest(new { message = "اختر ملفًا غير فارغ." });
        if (file.Length > MaxFileSize) return StatusCode(StatusCodes.Status413PayloadTooLarge);

        var extension = Path.GetExtension(Path.GetFileName(file.FileName));
        if (!AllowedExtensions.TryGetValue(extension, out var type))
            return BadRequest(new { message = "نوع الملف غير مدعوم." });
        if (extension.Equals(".webm", StringComparison.OrdinalIgnoreCase) && file.ContentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)) type = "audio";
        if (type == "video" && file.Length > 20 * 1024 * 1024) return BadRequest(new { message = "الحد الأقصى للفيديو 20 ميجابايت." });
        if (type == "audio" && file.Length > 10 * 1024 * 1024) return BadRequest(new { message = "حجم المقطع الصوتي كبير جداً." });
        var expectedPrefix = type + "/";
        if (!file.ContentType.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase) && file.ContentType != "application/octet-stream")
            return BadRequest(new { message = "نوع الملف لا يطابق امتداده." });
        if (!await HasExpectedSignature(file, extension, cancellationToken))
            return BadRequest(new { message = "محتوى الملف لا يطابق نوعه المعلن." });

        var supabaseUrl = configuration["SUPABASE_URL"] ?? configuration["Supabase:Url"];
        var supabaseKey = configuration["SUPABASE_SERVICE_ROLE_KEY"] ?? configuration["Supabase:ServiceRoleKey"];
        var bucket = configuration["SUPABASE_STORAGE_BUCKET"] ?? configuration["Supabase:Bucket"] ?? "shoura-media";
        if (!string.IsNullOrWhiteSpace(supabaseUrl) && !string.IsNullOrWhiteSpace(supabaseKey))
        {
            var objectName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("apikey", supabaseKey);
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", supabaseKey);
            await using var input = file.OpenReadStream();
            using var body = new StreamContent(input);
            body.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);
            var response = await client.PostAsync($"{supabaseUrl.TrimEnd('/')}/storage/v1/object/{Uri.EscapeDataString(bucket)}/{Uri.EscapeDataString(objectName)}", body, cancellationToken);
            if (!response.IsSuccessStatusCode) return StatusCode(StatusCodes.Status502BadGateway, new { message = "تعذر رفع الملف إلى التخزين." });
            var publicUrl = $"{supabaseUrl.TrimEnd('/')}/storage/v1/object/public/{Uri.EscapeDataString(bucket)}/{Uri.EscapeDataString(objectName)}";
            return Ok(new MediaUploadDto(publicUrl, type, file.Length));
        }

        var configuredPath = configuration["Media:StoragePath"] ?? Environment.GetEnvironmentVariable("SHOURA_MEDIA_PATH");
        var root = string.IsNullOrWhiteSpace(configuredPath) ? Path.Combine(environment.ContentRootPath, "uploads") : Path.GetFullPath(configuredPath);
        Directory.CreateDirectory(root);
        var storedName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var destination = Path.Combine(root, storedName);
        await using (var output = System.IO.File.Create(destination))
            await file.CopyToAsync(output, cancellationToken);

        if (!OperatingSystem.IsWindows())
            System.IO.File.SetUnixFileMode(destination, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        return Ok(new MediaUploadDto($"/api/media/{storedName}", type, file.Length));
    }

    [AllowAnonymous]
    [HttpGet("{fileName}")]
    [HttpGet("/media/{fileName}")]
    public async Task<IActionResult> GetMedia(string fileName, CancellationToken cancellationToken)
    {
        if (!MediaReference.TryGetStoredFileName($"/api/media/{fileName}", out _) ||
            !StringComparer.OrdinalIgnoreCase.Equals(Path.GetFileName(fileName), fileName)) return NotFound();

        var currentUserId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedUserId) ? parsedUserId : 0;
        var apiUrl = $"/api/media/{fileName}";
        var legacyUrl = $"/media/{fileName}";
        var isPublic = await db.Posts.AsNoTracking().AnyAsync(p => p.MediaUrl == apiUrl || p.MediaUrl == legacyUrl, cancellationToken);
        if (!isPublic)
        {
            var isConversationParticipant = currentUserId > 0 && await db.Messages.AsNoTracking().AnyAsync(m =>
                (m.MediaUrl == apiUrl || m.MediaUrl == legacyUrl) &&
                (m.SenderId == currentUserId || m.ReceiverId == currentUserId), cancellationToken);
            if (!isConversationParticipant) return NotFound();
        }

        var configuredPath = configuration["Media:StoragePath"] ?? Environment.GetEnvironmentVariable("SHOURA_MEDIA_PATH");
        var root = string.IsNullOrWhiteSpace(configuredPath) ? Path.Combine(environment.ContentRootPath, "uploads") : Path.GetFullPath(configuredPath);
        var fullPath = Path.GetFullPath(Path.Combine(root, fileName));
        if (!fullPath.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(fullPath)) return NotFound();
        var contentType = Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".mp4" => "video/mp4", ".webm" => "video/webm", ".mov" => "video/quicktime",
            ".mp3" => "audio/mpeg", ".m4a" => "audio/mp4", ".aac" => "audio/aac",
            ".wav" => "audio/wav", ".ogg" => "audio/ogg", ".opus" => "audio/opus",
            _ => "application/octet-stream"
        };
        return PhysicalFile(fullPath, contentType, enableRangeProcessing: true);
    }

    private static async Task<bool> HasExpectedSignature(IFormFile file, string extension, CancellationToken cancellationToken)
    {
        var header = new byte[12];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAsync(header.AsMemory(), cancellationToken);
        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => read >= 3 && header[0] == 0xff && header[1] == 0xd8 && header[2] == 0xff,
            ".png" => read >= 8 && header.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            ".webp" => read >= 12 && header.AsSpan(0, 4).SequenceEqual("RIFF"u8) && header.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            ".gif" => read >= 6 && (header.AsSpan(0, 6).SequenceEqual("GIF87a"u8) || header.AsSpan(0, 6).SequenceEqual("GIF89a"u8)),
            ".mp4" or ".mov" or ".m4a" => read >= 8 && header.AsSpan(4, 4).SequenceEqual("ftyp"u8),
            ".webm" => read >= 4 && header.AsSpan(0, 4).SequenceEqual(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }),
            ".wav" => read >= 12 && header.AsSpan(0, 4).SequenceEqual("RIFF"u8) && header.AsSpan(8, 4).SequenceEqual("WAVE"u8),
            ".ogg" or ".opus" => read >= 4 && header.AsSpan(0, 4).SequenceEqual("OggS"u8),
            ".mp3" => read >= 3 && (header.AsSpan(0, 3).SequenceEqual("ID3"u8) || (header[0] == 0xFF && (header[1] & 0xE0) == 0xE0)),
            ".aac" => read >= 2 && header[0] == 0xFF && (header[1] & 0xF6) == 0xF0,
            _ => false
        };
    }
}

public sealed record MediaUploadDto(string Url, string MediaType, long SizeBytes);
