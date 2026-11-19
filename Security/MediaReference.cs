using System.Text.RegularExpressions;

namespace Shoura.API.Security;

public static partial class MediaReference
{
    private static readonly Regex StoredName = new(
        @"^[a-f0-9]{32}\.(mp4|webm|mov|mp3|m4a|aac|wav|ogg|opus)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryGetStoredFileName(string? mediaUrl, out string fileName)
    {
        fileName = string.Empty;
        if (string.IsNullOrWhiteSpace(mediaUrl)) return false;
        var prefixLength = mediaUrl.StartsWith("/api/media/", StringComparison.Ordinal) ? 11
            : mediaUrl.StartsWith("/media/", StringComparison.Ordinal) ? 7 : 0;
        if (prefixLength == 0) return false;
        var candidate = mediaUrl[prefixLength..];
        if (candidate.Contains('/') || candidate.Contains('\\') || !StoredName.IsMatch(candidate)) return false;
        fileName = candidate;
        return true;
    }

    public static bool MatchesType(string fileName, string mediaType)
    {
        var extension = Path.GetExtension(fileName);
        return mediaType switch
        {
            "video" => extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
                       extension.Equals(".webm", StringComparison.OrdinalIgnoreCase) ||
                       extension.Equals(".mov", StringComparison.OrdinalIgnoreCase),
            "audio" => new[] { ".mp3", ".m4a", ".aac", ".wav", ".ogg", ".opus" }
                .Contains(extension, StringComparer.OrdinalIgnoreCase),
            _ => false
        };
    }
}
