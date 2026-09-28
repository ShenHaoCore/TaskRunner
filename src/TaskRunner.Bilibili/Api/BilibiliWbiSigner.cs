using System.Security.Cryptography;
using System.Text;

namespace TaskRunner.Bilibili.Api;

/// <summary>
/// B 站 web 端 wbi 签名：nav.wbi_img 的 img/sub 钥匙经混淆表生成 mixin key，
/// 参数按 key 排序并追加 wts 后做 MD5 得到 w_rid。空间等接口强制要求。
/// </summary>
internal static class BilibiliWbiSigner
{
    private static readonly int[] MixinKeyEncTab =
    [
        46, 47, 18, 2, 53, 8, 23, 32, 15, 50, 10, 31, 58, 3, 45, 35,
        27, 43, 5, 49, 33, 9, 42, 19, 29, 28, 14, 39, 12, 38, 41, 13,
        37, 48, 7, 16, 24, 55, 40, 61, 26, 17, 0, 1, 60, 51, 30, 4,
        22, 25, 54, 21, 56, 59, 6, 63, 57, 62, 11, 36, 20, 34, 44, 52
    ];

    /// <summary>根据 img_url/sub_url 提取钥匙并生成签名查询串（含 wts、w_rid）。</summary>
    public static string Sign(IDictionary<string, string?> parameters, string? imgUrl, string? subUrl, long? wtsOverride = null)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (string.IsNullOrWhiteSpace(imgUrl) || string.IsNullOrWhiteSpace(subUrl))
        {
            throw new InvalidOperationException("wbi 签名缺少 img_key/sub_key（nav.wbi_img 为空）。");
        }

        var imgKey = KeyFromUrl(imgUrl);
        var subKey = KeyFromUrl(subUrl);
        var mixinKey = GetMixinKey(imgKey + subKey);

        var signed = new SortedDictionary<string, string?>(parameters, StringComparer.Ordinal)
        {
            ["wts"] = (wtsOverride ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds()).ToString()
        };

        var query = string.Join(
            '&',
            signed.Where(p => !string.IsNullOrEmpty(p.Value))
                .Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value!)}"));

        var wRid = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(query + mixinKey))).ToLowerInvariant();
        return query + "&w_rid=" + wRid;
    }

    /// <summary>取图片 URL 中 wbi/ 之后、.png 之前的部分作为钥匙。</summary>
    private static string KeyFromUrl(string url)
    {
        var name = url.Split('/').Last();
        var dot = name.IndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }

    private static string GetMixinKey(string raw)
    {
        Span<char> buffer = stackalloc char[32];
        for (var i = 0; i < 32; i++)
        {
            buffer[i] = raw[MixinKeyEncTab[i]];
        }

        return buffer.ToString();
    }
}
