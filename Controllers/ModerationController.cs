using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shoura.API.Data;
using Shoura.API.Models;

namespace Shoura.API.Controllers;

[ApiController]
[Authorize]
[Route("api/moderation")]
public sealed class ModerationController(AppDbContext db, IConfiguration configuration) : ControllerBase
{
    private static readonly HashSet<string> Categories = new(StringComparer.Ordinal)
    {
        "sexual_content", "child_safety", "harassment", "threats", "spam", "other", "ai_output"
    };

    [HttpPost("reports")]
    public async Task<IActionResult> SubmitReport(SubmitReportDto request, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var reporterId)) return Unauthorized();
        if (!Categories.Contains(request.Category)) return BadRequest(new { message = "اختر سببًا صالحًا للبلاغ." });

        int? targetUserId = request.TargetUserId;
        if (request.MessageId is int messageId)
        {
            var message = await db.Messages.AsNoTracking().SingleOrDefaultAsync(m => m.Id == messageId &&
                (m.SenderId == reporterId || m.ReceiverId == reporterId), cancellationToken);
            if (message is null) return NotFound();
            targetUserId ??= message.SenderId == reporterId ? message.ReceiverId : message.SenderId;
        }

        if (targetUserId is int userId && (!await db.Users.AnyAsync(u => u.Id == userId, cancellationToken) || userId == reporterId))
            return BadRequest(new { message = "الحساب المبلّغ عنه غير صالح." });
        if (targetUserId is null && request.MessageId is null && request.Category != "ai_output")
            return BadRequest(new { message = "حدد المستخدم أو الرسالة محل البلاغ." });
        if (request.MessageId is not null && request.TargetUserId is not null && targetUserId != request.TargetUserId)
            return BadRequest(new { message = "الحساب لا يطابق الرسالة المحددة." });

        db.ContentReports.Add(new ContentReport
        {
            ReporterUserId = reporterId,
            TargetUserId = targetUserId,
            MessageId = request.MessageId,
            Category = request.Category,
            Details = request.Details?.Trim()
        });
        await db.SaveChangesAsync(cancellationToken);
        return Accepted(new { message = "وصل البلاغ إلى فريق مراجعة شورى." });
    }

    [HttpGet("reports")]
    public async Task<IActionResult> GetOpenReports(CancellationToken cancellationToken)
    {
        if (!IsAdministrator()) return Forbid();
        var reports = await db.ContentReports.AsNoTracking().Where(r => r.Status == "open")
            .OrderBy(r => r.CreatedAtUtc).Take(200)
            .Select(r => new ModerationReportDto(r.Id, r.ReporterUserId, r.TargetUserId, r.MessageId, r.Category, r.Details, r.CreatedAtUtc))
            .ToListAsync(cancellationToken);
        return Ok(reports);
    }

    [HttpDelete("reports/{reportId}/message")]
    public async Task<IActionResult> RemoveReportedMessage(string reportId, CancellationToken cancellationToken)
    {
        if (!IsAdministrator()) return Forbid();
        var report = await db.ContentReports.SingleOrDefaultAsync(r => r.Id == reportId && r.Status == "open", cancellationToken);
        if (report is null) return NotFound();
        if (report.MessageId is int messageId)
        {
            var message = await db.Messages.SingleOrDefaultAsync(m => m.Id == messageId, cancellationToken);
            if (message is not null) db.Messages.Remove(message);
        }
        report.Status = "resolved";
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private bool IsAdministrator()
    {
        if (!TryGetUserId(out var userId)) return false;
        return (configuration.GetSection("Moderation:AdminUserIds").Get<int[]>() ?? []).Contains(userId);
    }

    private bool TryGetUserId(out int userId) => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId) && userId > 0;
}

public sealed class SubmitReportDto
{
    [Range(1, int.MaxValue)] public int? TargetUserId { get; set; }
    [Range(1, int.MaxValue)] public int? MessageId { get; set; }
    [Required, StringLength(32)] public string Category { get; set; } = string.Empty;
    [StringLength(1000)] public string? Details { get; set; }
}

public sealed record ModerationReportDto(string Id, int ReporterUserId, int? TargetUserId, int? MessageId, string Category, string? Details, DateTime CreatedAtUtc);
