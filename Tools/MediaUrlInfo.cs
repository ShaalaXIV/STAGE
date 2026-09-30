namespace STAGE.Tools;

public enum MediaService
{
    Unknown,
    YouTube,
    SoundCloud,
    Other
}

public static class MediaUrlInfo
{
    public static MediaService Classify(string? url)
    {
        if (!TryParse(url, out var uri)) return MediaService.Unknown;
        string host = uri!.Host;
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) host = host[4..];
        if (Matches(host, "youtube.com") || Matches(host, "youtu.be") || Matches(host, "youtube-nocookie.com")) return MediaService.YouTube;
        if (Matches(host, "soundcloud.com") || Matches(host, "snd.sc")) return MediaService.SoundCloud;
        return MediaService.Other;
    }

    public static bool TryValidate(string? url, out string cleaned)
    {
        cleaned = url?.Trim() ?? string.Empty;
        if (cleaned.Length == 0 || cleaned.IndexOf('"') >= 0 || cleaned.IndexOf('\\') >= 0 || cleaned.Any(char.IsControl)) return false;
        return TryParse(cleaned, out _);
    }

    private static bool TryParse(string? url, out Uri? uri)
    {
        uri = null;
        return Uri.TryCreate(url?.Trim(), UriKind.Absolute, out uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static bool Matches(string host, string domain) =>
        host.Equals(domain, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
}
