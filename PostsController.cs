using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Shoura.API.Data;
using Shoura.API.Models;
using Shoura.API.Security;

namespace Shoura.API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PostsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<FeedPageDto>> GetPosts([FromQuery] string? hashtag, [FromQuery] int limit = 30, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 50);
        var query = db.Posts.AsNoTracking().Include(p => p.User).Include(p => p.Comments).ThenInclude(c => c.User).OrderByDescending(p => p.CreatedAt);
        var posts = await query.Take(200).ToListAsync(ct);
        if (!string.IsNullOrWhiteSpace(hashtag)) posts = posts.Where(p => p.Content.Contains("#" + hashtag.TrimStart('#'), StringComparison.OrdinalIgnoreCase)).ToList();
        var items = posts.Take(limit).Select(p => ToDto(p)).ToList();
        return new FeedPageDto(items, null, null, posts.Count > limit);
    }

    [HttpPost, Authorize]
    public async Task<ActionResult<PostDto>> CreatePost(CreatePostDto model, CancellationToken ct)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (string.IsNullOrWhiteSpace(model.Content) && string.IsNullOrWhiteSpace(model.MediaUrl)) return BadRequest(new { message = "اكتب شيئاً أو أرفق وسائط." });
        if (model.MediaType is not ("text" or "image" or "video" or "audio")) return BadRequest();
        if (!string.IsNullOrWhiteSpace(model.MediaUrl) || model.MediaType != "text")
        {
            var email = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Email).SingleOrDefaultAsync(ct);
            if (GuestAccounts.IsGuestEmail(email)) return StatusCode(StatusCodes.Status403Forbidden, new { message = GuestAccounts.UploadMessage });
        }
        if (model.MediaType == "video" && model.MediaSizeBytes > 20 * 1024 * 1024) return BadRequest(new { message = "الحد الأقصى للفيديو 20 ميجابايت." });
        var content = model.Content.Trim();
        var hashtags = System.Text.RegularExpressions.Regex.Matches(content, @"(?<!\w)#([\p{L}\d_]+)")
            .Select(m => m.Groups[1].Value.ToLowerInvariant()).Distinct().ToArray();
        var post = new Post { Content = content, Hashtags = string.Join(" ", hashtags), MediaUrl = model.MediaUrl, MediaType = model.MediaType, UserId = userId };
        db.Posts.Add(post); await db.SaveChangesAsync(ct);
        await db.Entry(post).Reference(p => p.User).LoadAsync(ct);
        return Ok(ToDto(post));
    }

    [HttpPost("{id:int}/like"), Authorize]
    public async Task<IActionResult> Like(int id, CancellationToken ct)
    {
        var post = await db.Posts.FindAsync([id], ct); if (post is null) return NotFound();
        post.LikesCount++; await db.SaveChangesAsync(ct); return Ok(new { likesCount = post.LikesCount });
    }

    [HttpPost("{id:int}/repost"), Authorize]
    public async Task<IActionResult> Repost(int id, CancellationToken ct)
    {
        var post = await db.Posts.FindAsync([id], ct); if (post is null) return NotFound();
        post.RetweetsCount++; await db.SaveChangesAsync(ct); return Ok(new { retweetsCount = post.RetweetsCount });
    }

    [HttpPost("{id:int}/comments"), Authorize]
    public async Task<ActionResult<CommentDto>> Comment(int id, CreateCommentDto model, CancellationToken ct)
    {
        if (!await db.Posts.AnyAsync(p => p.Id == id, ct)) return NotFound();
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var comment = new Comment { PostId = id, UserId = userId, Content = model.Content.Trim() };
        db.Comments.Add(comment); await db.SaveChangesAsync(ct); await db.Entry(comment).Reference(c => c.User).LoadAsync(ct);
        return Ok(new CommentDto(comment.Id, comment.Content, comment.CreatedAt, comment.User!.Name, comment.User.Handle));
    }

    private static PostDto ToDto(Post p) => new(p.Id, p.Content, p.MediaUrl, p.MediaType, p.LikesCount, p.RetweetsCount, p.CreatedAt, p.UserId,
        p.User is null ? null : new PublicUserDto(p.User.Id, p.User.Name, p.User.Handle, p.User.Bio),
        p.Comments.OrderBy(c => c.CreatedAt).Select(c => new CommentDto(c.Id, c.Content, c.CreatedAt, c.User?.Name ?? "مستخدم", c.User?.Handle ?? "user")).ToList());
}

public class CreatePostDto
{
    [StringLength(10_000)] public string Content { get; set; } = string.Empty;
    [StringLength(2_048)] public string? MediaUrl { get; set; }
    [RegularExpression("^(text|image|video|audio)$")] public string MediaType { get; set; } = "text";
    public long MediaSizeBytes { get; set; }
}
public sealed class CreateCommentDto { [Required, StringLength(2000, MinimumLength = 1)] public string Content { get; set; } = string.Empty; }
public sealed record PublicUserDto(int Id, string Name, string Handle, string Bio);
public sealed record CommentDto(int Id, string Content, DateTime CreatedAt, string Name, string Handle);
public sealed record PostDto(int Id, string Content, string? MediaUrl, string MediaType, int LikesCount, int RetweetsCount, DateTime CreatedAt, int UserId, PublicUserDto? User, IReadOnlyList<CommentDto> Comments);
public sealed record FeedPageDto(IReadOnlyList<PostDto> Items, int? NextCursor, DateTime? NextCursorCreatedAt, bool HasMore);
