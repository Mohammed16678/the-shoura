using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shoura.API.Data;
using Shoura.API.Models;
using Shoura.API.Security;

namespace Shoura.API.Controllers;

[ApiController]
[Authorize]
[Route("api/messages")]
public sealed class MessagesController(AppDbContext db, IConfiguration configuration, IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet("{otherUserId:int}")]
    public async Task<ActionResult<IReadOnlyList<MessageDto>>> GetConversation(int otherUserId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var currentUserId)) return Unauthorized();
        var exists = await db.Users.AnyAsync(u => u.Id == otherUserId, cancellationToken);
        if (!exists) return NotFound(new { message = "المستخدم غير موجود." });
        if (await IsBlocked(currentUserId, otherUserId, cancellationToken)) return NotFound();

        var messages = await db.Messages.AsNoTracking()
            .Where(m => (m.SenderId == currentUserId && m.ReceiverId == otherUserId) ||
                        (m.SenderId == otherUserId && m.ReceiverId == currentUserId))
            .OrderBy(m => m.SentAt)
            .Take(300)
            .Select(m => new MessageDto(m.Id, m.SenderId, m.ReceiverId, m.Content, m.MediaUrl, m.MediaType, m.SentAt))
            .ToListAsync(cancellationToken);
        return Ok(messages);
    }

    [HttpPost]
    public async Task<ActionResult<MessageDto>> SendMessage(SendMessageDto request, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var senderId)) return Unauthorized();
        var sender = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == senderId, cancellationToken);
        if (sender is null) return Unauthorized();
        if (sender.AcceptedTermsVersion != AuthController.CurrentTermsVersion)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "وافق على الشروط وسياسة الخصوصية من شاشة تسجيل الدخول قبل استخدام الرسائل." });
        if (await IsBlocked(senderId, request.ReceiverId, cancellationToken)) return NotFound();
        if (request.ReceiverId == senderId) return BadRequest(new { message = "لا يمكن إرسال رسالة إلى حسابك." });
        if (!await db.Users.AnyAsync(u => u.Id == request.ReceiverId, cancellationToken))
            return NotFound(new { message = "المستخدم المستلم غير موجود." });
        if (string.IsNullOrWhiteSpace(request.Content) && string.IsNullOrWhiteSpace(request.MediaUrl))
            return BadRequest(new { message = "أضف نصًا أو ملفًا صوتيًا للرسالة." });
        if (request.MediaType is not ("text" or "audio"))
            return BadRequest(new { message = "نوع المرفق غير مدعوم في الرسائل." });
        if (string.IsNullOrWhiteSpace(request.MediaUrl))
        {
            if (request.MediaType != "text") return BadRequest(new { message = "نوع الرسالة لا يطابق المرفق." });
        }
        else
        {
            var configuredPath = configuration["Media:StoragePath"] ?? Environment.GetEnvironmentVariable("SHOURA_MEDIA_PATH");
            var root = string.IsNullOrWhiteSpace(configuredPath)
                ? Path.Combine(environment.ContentRootPath, "uploads")
                : Path.GetFullPath(configuredPath);
            if (request.MediaType != "audio" ||
                !MediaReference.TryGetStoredFileName(request.MediaUrl, out var mediaFile) ||
                !MediaReference.MatchesType(mediaFile, "audio") ||
                !System.IO.File.Exists(Path.Combine(Path.GetFullPath(root), mediaFile)))
                return BadRequest(new { message = "يجب إرفاق ملف صوتي صالح تم رفعه إلى شورى." });
        }

        var message = new Message
        {
            SenderId = senderId,
            ReceiverId = request.ReceiverId,
            Content = request.Content?.Trim() ?? string.Empty,
            MediaUrl = request.MediaUrl,
            MediaType = request.MediaType,
            SentAt = DateTime.UtcNow
        };
        db.Messages.Add(message);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new MessageDto(message.Id, message.SenderId, message.ReceiverId, message.Content, message.MediaUrl, message.MediaType, message.SentAt));
    }

    private bool TryGetUserId(out int id) => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out id) && id > 0;

    private Task<bool> IsBlocked(int firstUserId, int secondUserId, CancellationToken cancellationToken) => db.UserBlocks.AnyAsync(b =>
        (b.UserId == firstUserId && b.BlockedUserId == secondUserId) ||
        (b.UserId == secondUserId && b.BlockedUserId == firstUserId), cancellationToken);
}

public sealed class SendMessageDto
{
    [Range(1, int.MaxValue)] public int ReceiverId { get; set; }
    [StringLength(5000)] public string? Content { get; set; }
    [StringLength(2048)] public string? MediaUrl { get; set; }
    [StringLength(16)] public string MediaType { get; set; } = "text";
}

public sealed record MessageDto(int Id, int SenderId, int ReceiverId, string Content, string? MediaUrl, string MediaType, DateTime SentAt);
