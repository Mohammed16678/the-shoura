using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Shoura.API.Data;

namespace Shoura.API.Security;

public sealed class BearerTokenHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly BearerTokenService _tokens;
    private readonly AppDbContext _db;

    public BearerTokenHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        BearerTokenService tokens,
        AppDbContext db) : base(options, logger, encoder)
    {
        _tokens = tokens;
        _db = db;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        if (!_tokens.TryValidate(header[7..].Trim(), out var userId, out var sessionVersion))
            return AuthenticateResult.Fail("Invalid or expired access token.");

        var activeSessionVersion = await _db.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => (int?)user.SessionVersion)
            .SingleOrDefaultAsync(Context.RequestAborted);
        if (activeSessionVersion is null || activeSessionVersion.Value != sessionVersion)
            return AuthenticateResult.Fail("Session has been revoked.");

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim("session_version", sessionVersion.ToString())], Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}
