using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shoura.API.Data;
using Shoura.API.Models;

namespace Shoura.API.Controllers;

[ApiController]
[Authorize]
[Route("api/users")]
public sealed class UsersController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ContactDto>>> GetContacts(CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var currentUserId)) return Unauthorized();
        var users = await db.Users.AsNoTracking()
            .Where(u => u.Id != currentUserId && !db.UserBlocks.Any(b =>
                (b.UserId == currentUserId && b.BlockedUserId == u.Id) ||
                (b.UserId == u.Id && b.BlockedUserId == currentUserId)))
            .OrderBy(u => u.Handle)
            .Take(100)
            .Select(u => new ContactDto(u.Id, u.Name, u.Handle, u.Bio))
            .ToListAsync(cancellationToken);
        return Ok(users);
    }

    [HttpPost("{targetUserId:int}/block")]
    public async Task<IActionResult> BlockUser(int targetUserId, CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var currentUserId)) return Unauthorized();
        if (targetUserId == currentUserId) return BadRequest(new { message = "لا يمكنك حظر حسابك." });
        if (!await db.Users.AnyAsync(u => u.Id == targetUserId, cancellationToken)) return NotFound();
        if (!await db.UserBlocks.AnyAsync(b => b.UserId == currentUserId && b.BlockedUserId == targetUserId, cancellationToken))
        {
            db.UserBlocks.Add(new UserBlock { UserId = currentUserId, BlockedUserId = targetUserId });
            await db.SaveChangesAsync(cancellationToken);
        }
        return NoContent();
    }

    [HttpDelete("{targetUserId:int}/block")]
    public async Task<IActionResult> UnblockUser(int targetUserId, CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var currentUserId)) return Unauthorized();
        var block = await db.UserBlocks.SingleOrDefaultAsync(b => b.UserId == currentUserId && b.BlockedUserId == targetUserId, cancellationToken);
        if (block is null) return NoContent();
        db.UserBlocks.Remove(block);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}

public sealed record ContactDto(int Id, string Name, string Handle, string Bio);
