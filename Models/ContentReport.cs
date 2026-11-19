namespace Shoura.API.Models;

public sealed class ContentReport
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int ReporterUserId { get; set; }
    public int? TargetUserId { get; set; }
    public int? MessageId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string? Details { get; set; }
    public string Status { get; set; } = "open";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
