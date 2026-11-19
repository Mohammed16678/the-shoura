using System.ComponentModel.DataAnnotations;

namespace Shoura.API.Models;

public class Message
{
    public int Id { get; set; }
    public int SenderId { get; set; }
    public int ReceiverId { get; set; }
    [MaxLength(5000)]
    public string Content { get; set; } = string.Empty;
    [MaxLength(2048)]
    public string? MediaUrl { get; set; }
    [MaxLength(16)]
    public string MediaType { get; set; } = "text";
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}
