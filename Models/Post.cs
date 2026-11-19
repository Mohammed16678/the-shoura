namespace Shoura.API.Models;

public class Post
{
    public int Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public string Hashtags { get; set; } = string.Empty;
    
    // دعم الوسائط (صورة، فيديو، مقطع صوتي)
    public string? MediaUrl { get; set; }
    public string MediaType { get; set; } = "text"; // "text", "image", "video", "audio"
    
    public int LikesCount { get; set; } = 0;
    public int RetweetsCount { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int UserId { get; set; }
    public User? User { get; set; }
    public ICollection<Comment> Comments { get; set; } = new List<Comment>();
}
