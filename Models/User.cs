namespace Shoura.API.Models;

public class User
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Bio { get; set; } = string.Empty;
    public string? AcceptedTermsVersion { get; set; }
    public int SessionVersion { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
