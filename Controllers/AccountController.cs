using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shoura.API.Data;
using Shoura.API.Security;

namespace Shoura.API.Controllers;

[ApiController]
[Route("api/account")]
[Authorize]
public sealed class AccountController(AppDbContext db, IConfiguration configuration, IWebHostEnvironment environment) : ControllerBase
{
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null) return NoContent();
        checked { user.SessionVersion++; }
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("me")]
    public async Task<IActionResult> DeleteMyAccount(CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null) return NoContent();
        var mediaUrls = await db.Posts.Where(p => p.UserId == userId && p.MediaUrl != null).Select(p => p.MediaUrl!).ToListAsync(cancellationToken);
        mediaUrls.AddRange(await db.Messages.Where(m => (m.SenderId == userId || m.ReceiverId == userId) && m.MediaUrl != null)
            .Select(m => m.MediaUrl!).ToListAsync(cancellationToken));
        db.Users.Remove(user); // حذف المنشورات والرسائل والتعليقات المرتبطة عبر قيود Cascade.
        await db.SaveChangesAsync(cancellationToken);
        var mediaRoot = configuration["Media:StoragePath"] ?? Environment.GetEnvironmentVariable("SHOURA_MEDIA_PATH");
        if (string.IsNullOrWhiteSpace(mediaRoot)) mediaRoot = Path.Combine(environment.ContentRootPath, "uploads");
        foreach (var mediaUrl in mediaUrls.Distinct(StringComparer.Ordinal))
        {
            var fileName = Path.GetFileName(mediaUrl);
            var extension = Path.GetExtension(fileName);
            if (!MediaReference.TryGetStoredFileName(mediaUrl, out _) ||
                !Guid.TryParseExact(Path.GetFileNameWithoutExtension(fileName), "N", out _) ||
                await db.Posts.AnyAsync(p => p.MediaUrl == mediaUrl, cancellationToken) ||
                await db.Messages.AnyAsync(m => m.MediaUrl == mediaUrl, cancellationToken)) continue;
            if (new[] { ".mp4", ".webm", ".mov", ".mp3", ".m4a", ".aac", ".wav", ".ogg", ".opus" }
                .Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                var filePath = Path.Combine(Path.GetFullPath(mediaRoot), fileName);
                if (System.IO.File.Exists(filePath)) System.IO.File.Delete(filePath);
            }
        }
        return NoContent();
    }
}
