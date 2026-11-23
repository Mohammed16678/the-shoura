using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shoura.API.Data;
using Shoura.API.Models;
using Shoura.API.Security;

namespace Shoura.API.Controllers;

[Route("api/[controller]")]
[ApiController]
[EnableRateLimiting("auth")]
public class AuthController : ControllerBase
{
    private const int PasswordIterations = 600_000;
    public const string CurrentTermsVersion = "2026-11-18-v1";
    private readonly AppDbContext _context;
    private readonly BearerTokenService _tokens;

    public AuthController(AppDbContext context, BearerTokenService tokens)
    {
        _context = context;
        _tokens = tokens;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthUserDto>> Register(RegisterDto model, CancellationToken cancellationToken)
    {
        if (!model.AcceptTerms) return BadRequest(new { message = "يجب الموافقة على شروط الاستخدام وسياسة الخصوصية لإنشاء الحساب." });
        var email = model.Email.Trim().ToLowerInvariant();
        var handle = model.Handle.Trim().TrimStart('@').ToLowerInvariant();
        if (await _context.Users.AnyAsync(u => u.Email == email || u.Handle == handle, cancellationToken))
            return Conflict(new { message = "البريد الإلكتروني أو اسم المستخدم مستخدم مسبقاً." });

        var user = new User
        {
            Name = model.Name.Trim(),
            Handle = handle,
            Email = email,
            Bio = model.Bio?.Trim() ?? "مرحباً بك في موعظة",
            AcceptedTermsVersion = CurrentTermsVersion,
            PasswordHash = HashPassword(model.Password)
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(user));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthUserDto>> Login(LoginDto model, CancellationToken cancellationToken)
    {
        var email = model.Email.Trim().ToLowerInvariant();
        var user = await _context.Users.SingleOrDefaultAsync(u => u.Email == email, cancellationToken);
        if (user is null || !VerifyPassword(model.Password, user.PasswordHash))
            return Unauthorized(new { message = "البريد الإلكتروني أو كلمة المرور غير صحيحة." });

        if (model.AcceptTerms && user.AcceptedTermsVersion != CurrentTermsVersion)
        {
            user.AcceptedTermsVersion = CurrentTermsVersion;
            await _context.SaveChangesAsync(cancellationToken);
        }

        return Ok(ToResponse(user));
    }

    private static AuthUserDto ToDto(User user) => new(user.Id, user.Name, user.Handle, user.Email, user.Bio);

    private AuthResponseDto ToResponse(User user)
    {
        var (token, expiresAtUtc) = _tokens.Create(user.Id, user.SessionVersion);
        return new AuthResponseDto(ToDto(user), token, expiresAtUtc);
    }

    private static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, PasswordIterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2-sha256${PasswordIterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPassword(string password, string storedHash)
    {
        var parts = storedHash.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha256" ||
            !int.TryParse(parts[1], out var iterations) || iterations is < 100_000 or > 1_000_000)
            return false;

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public sealed class RegisterDto
{
    public bool AcceptTerms { get; set; }
    [Required, StringLength(80, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [Required, RegularExpression("^[A-Za-z0-9_@.]{3,30}$")]
    public string Handle { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 12)]
    public string Password { get; set; } = string.Empty;

    [StringLength(240)]
    public string? Bio { get; set; }
}

public sealed class LoginDto
{
    public bool AcceptTerms { get; set; }
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 12)]
    public string Password { get; set; } = string.Empty;
}

public sealed record AuthUserDto(int Id, string Name, string Handle, string Email, string Bio);
public sealed record AuthResponseDto(AuthUserDto User, string AccessToken, DateTime ExpiresAtUtc);
