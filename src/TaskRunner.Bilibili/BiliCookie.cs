namespace TaskRunner.Bilibili;

public sealed class BiliCookie
{
    public BiliCookie(string raw)
    {
        Raw = raw?.Trim() ?? string.Empty;
        Values = Parse(Raw);
    }

    public string Raw { get; }

    public IReadOnlyDictionary<string, string> Values { get; }

    public string? SessData => Get("SESSDATA");

    public string? BiliJct => Get("bili_jct");

    public string? DedeUserId => Get("DedeUserID");

    public string? Buvid3 => Get("buvid3");

    public bool HasLoginTokens =>
        !string.IsNullOrWhiteSpace(SessData) && !string.IsNullOrWhiteSpace(BiliJct);

    public bool HasBuvid => !string.IsNullOrWhiteSpace(Buvid3);

    public string? Get(string name) =>
        Values.TryGetValue(name, out var value) ? value : null;

    public BiliCookie MergeSetCookies(IEnumerable<string> setCookieHeaders)
    {
        var map = new Dictionary<string, string>(Values, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in ParseSetCookieHeaders(setCookieHeaders))
        {
            map[pair.Key] = pair.Value;
        }

        return new BiliCookie(string.Join("; ", map.Select(p => $"{p.Key}={p.Value}")));
    }

    /// <summary>将 HTTP Set-Cookie 头转为浏览器风格 Cookie 串。</summary>
    public static string FromSetCookieHeaders(IEnumerable<string> setCookieHeaders)
        => string.Join("; ", ParseSetCookieHeaders(setCookieHeaders).Select(p => $"{p.Key}={p.Value}"));

    private static Dictionary<string, string> ParseSetCookieHeaders(IEnumerable<string> setCookieHeaders)
    {
        ArgumentNullException.ThrowIfNull(setCookieHeaders);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in setCookieHeaders)
        {
            if (string.IsNullOrWhiteSpace(header))
            {
                continue;
            }

            var first = header.Split(';', 2)[0].Trim();
            var index = first.IndexOf('=');
            if (index <= 0)
            {
                continue;
            }

            var key = first[..index].Trim();
            var value = first[(index + 1)..].Trim();
            if (key.Length == 0)
            {
                continue;
            }

            map[key] = value;
        }

        return map;
    }

    private static Dictionary<string, string> Parse(string raw)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return map;
        }

        foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var index = part.IndexOf('=');
            if (index <= 0)
            {
                continue;
            }

            var key = part[..index].Trim();
            var value = part[(index + 1)..].Trim();
            if (key.Length == 0)
            {
                continue;
            }

            map[key] = value;
        }

        return map;
    }
}
