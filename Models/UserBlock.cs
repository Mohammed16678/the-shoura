namespace Shoura.API.Models;

public sealed class UserBlock
{
    public int UserId { get; set; }
    public int BlockedUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
