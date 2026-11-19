using System.Security.Cryptography;
using System.Text;

namespace Shoura.API.Security;

public sealed class BearerTokenService(byte[] signingKey)
{
    private readonly byte[] _signingKey = signingKey;

    public (string Token, DateTime ExpiresAtUtc) Create(int userId, int sessionVersion)
    {
        var expiry = DateTime.UtcNow.AddHours(2);
        var sessionNonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var payload = Base64Url(Encoding.UTF8.GetBytes($"{userId}:{new DateTimeOffset(expiry).ToUnixTimeSeconds()}:{sessionVersion}:{sessionNonce}"));
        var signature = HMACSHA256.HashData(_signingKey, Encoding.UTF8.GetBytes(payload));
        return ($"{payload}.{Base64Url(signature)}", expiry);
    }

    public bool TryValidate(string token, out int userId, out int sessionVersion)
    {
        userId = 0;
        sessionVersion = -1;
        if (string.IsNullOrWhiteSpace(token) || token.Length > 2048) return false;
        var segments = token.Split('.');
        if (segments.Length != 2) return false;

        try
        {
            var expected = HMACSHA256.HashData(_signingKey, Encoding.UTF8.GetBytes(segments[0]));
            var actual = FromBase64Url(segments[1]);
            if (actual.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(actual, expected)) return false;

            var values = Encoding.UTF8.GetString(FromBase64Url(segments[0])).Split(':');
            if (values.Length != 4 || !int.TryParse(values[0], out var parsedUserId) || parsedUserId <= 0 ||
                !long.TryParse(values[1], out var expiry) || expiry <= DateTimeOffset.UtcNow.ToUnixTimeSeconds() ||
                !int.TryParse(values[2], out var parsedSessionVersion) || parsedSessionVersion < 0 ||
                values[3].Length != 32 || !values[3].All(Uri.IsHexDigit)) return false;
            userId = parsedUserId;
            sessionVersion = parsedSessionVersion;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 += new string('=', (4 - base64.Length % 4) % 4);
        return Convert.FromBase64String(base64);
    }
}
