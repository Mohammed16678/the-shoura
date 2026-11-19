using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Shoura.API.Data;
using Shoura.API.Security;

namespace Shoura.API.Controllers;

[ApiController]
[Authorize]
[EnableRateLimiting("ai")]
[Route("api/ai")]
public sealed class AiController(IHttpClientFactory clients, IConfiguration configuration, IWebHostEnvironment environment, AppDbContext db) : ControllerBase
{
    private const long MaxAiFileBytes = 10 * 1024 * 1024;

    [HttpPost("explain-video")]
    public Task<ActionResult<AiResultDto>> ExplainVideo(AiMediaRequest request, CancellationToken cancellationToken) =>
        Analyze(request.MediaUrl, "video", "اشرح محتوى هذا المقطع بالعربية بوضوح واختصار. اذكر أهم الأحداث أو الأفكار، ولا تخمّن ما لا يظهر في الفيديو.", cancellationToken);

    [HttpPost("translate-audio")]
    public Task<ActionResult<AiResultDto>> TranslateAudio(AiMediaRequest request, CancellationToken cancellationToken) =>
        Analyze(request.MediaUrl, "audio", "فرّغ الكلام المسموع بدقة، ثم قدم ترجمة عربية طبيعية. اعرض النص الأصلي والترجمة العربية بعناوين واضحة. إذا كان الكلام عربيًا، اعرض التفريغ العربي فقط.", cancellationToken);

    private async Task<ActionResult<AiResultDto>> Analyze(string mediaUrl, string expectedType, string prompt, CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var currentUserId)) return Unauthorized();
        if (!MediaReference.TryGetStoredFileName(mediaUrl, out var fileName))
            return BadRequest(new { message = "رابط الوسائط غير صالح." });

        var apiUrl = $"/api/media/{fileName}";
        var legacyUrl = $"/media/{fileName}";
        var canReadMedia = await db.Messages.AsNoTracking().AnyAsync(m =>
            (m.MediaUrl == apiUrl || m.MediaUrl == legacyUrl) &&
            (m.SenderId == currentUserId || m.ReceiverId == currentUserId), cancellationToken);
        if (!canReadMedia) return NotFound(new { message = "الملف غير موجود." });
        var apiKey = configuration["Gemini:ApiKey"] ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey)) return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "خدمة الذكاء الاصطناعي غير مهيأة على الخادم." });

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var isVideo = extension is ".mp4" or ".webm" or ".mov";
        if ((expectedType == "video") != isVideo) return BadRequest(new { message = "نوع الوسائط لا يطابق الطلب." });

        var configuredPath = configuration["Media:StoragePath"] ?? Environment.GetEnvironmentVariable("SHOURA_MEDIA_PATH");
        var root = string.IsNullOrWhiteSpace(configuredPath) ? Path.Combine(environment.ContentRootPath, "uploads") : Path.GetFullPath(configuredPath);
        var fullRoot = Path.GetFullPath(root);
        var filePath = Path.GetFullPath(Path.Combine(fullRoot, fileName));
        if (!filePath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "رابط الوسائط غير صالح." });
        var file = new FileInfo(filePath);
        if (!file.Exists) return NotFound(new { message = "الملف غير موجود." });
        if (file.Length > MaxAiFileBytes) return StatusCode(StatusCodes.Status413PayloadTooLarge, new { message = "تحليل الذكاء الاصطناعي متاح حاليًا للملفات حتى 10 ميغابايت." });

        var mimeType = extension switch
        {
            ".mp4" => isVideo ? "video/mp4" : "audio/mp4",
            ".webm" => "video/webm", ".mov" => "video/quicktime",
            ".mp3" => "audio/mp3", ".m4a" => "audio/m4a", ".aac" => "audio/aac",
            ".wav" => "audio/wav", ".ogg" => "audio/ogg", ".opus" => "audio/opus",
            _ => "application/octet-stream"
        };
        var bytes = await System.IO.File.ReadAllBytesAsync(filePath, cancellationToken);
        var model = configuration["Gemini:Model"] ?? Environment.GetEnvironmentVariable("GEMINI_MODEL") ?? "gemini-3.8-flash";
        var payload = JsonSerializer.Serialize(new
        {
            model,
            store = false,
            input = new object[]
            {
                new { type = "text", text = prompt },
                new { type = expectedType, data = Convert.ToBase64String(bytes), mime_type = mimeType }
            }
        });
        using var message = new HttpRequestMessage(HttpMethod.Post, "https://generativelanguage.googleapis.com/v1beta/interactions");
        message.Headers.Add("x-goog-api-key", apiKey);
        message.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await clients.CreateClient("Gemini").SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return StatusCode(StatusCodes.Status502BadGateway, new { message = "تعذر إكمال التحليل من مزود الذكاء الاصطناعي." });

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!json.RootElement.TryGetProperty("output_text", out var output) || string.IsNullOrWhiteSpace(output.GetString()))
            return StatusCode(StatusCodes.Status502BadGateway, new { message = "لم يرجع مزود الذكاء الاصطناعي نتيجة نصية." });
        return Ok(new AiResultDto(output.GetString()!, model));
    }
}

public sealed class AiMediaRequest
{
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(2048)]
    public string MediaUrl { get; set; } = string.Empty;
}

public sealed record AiResultDto(string Text, string Model);
