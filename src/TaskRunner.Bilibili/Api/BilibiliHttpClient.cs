using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TaskRunner.Bilibili.Models;

namespace TaskRunner.Bilibili.Api;

/// <summary>
/// B 站接口统一 HTTP 入口：集中处理 Cookie 注入、Referer/Origin 解析、wbi 签名、
/// 表单/JSON 提交以及「HTTP 4xx 但带业务 JSON」的容错读取。各领域 API 只声明意图，不直接拼请求。
/// </summary>
public sealed class BilibiliHttpClient
{
    public const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    private readonly HttpClient _http;
    private readonly IBilibiliCookieStore _cookieStore;

    public BilibiliHttpClient(HttpClient http, IBilibiliCookieStore cookieStore)
    {
        _http = http;
        _cookieStore = cookieStore;
    }

    public BilibiliCookie Cookie => new(_cookieStore.GetCookie());

    /// <summary>本次执行的 wbi 钥匙，Nav 成功后写入；wbi 接口签名时读取。</summary>
    public string? WbiImgKey { get; set; }

    public string? WbiSubKey { get; set; }

    /// <summary>访问主站补齐 buvid3 等设备 Cookie，并把新 Cookie 落盘。</summary>
    public async Task EnsureBrowserCookiesAsync(CancellationToken cancellationToken)
    {
        var before = Cookie;
        using var request = CreateRequest(HttpMethod.Get, BilibiliEndpoints.WwwHome, BilibiliEndpoints.WwwHome, BilibiliEndpoints.WwwOrigin);
        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            return;
        }

        var merged = before.MergeSetCookies(setCookies);
        if (string.Equals(merged.Raw, before.Raw, StringComparison.Ordinal))
        {
            return;
        }

        if (merged.IsAuthenticated)
        {
            await _cookieStore.SaveAsync(merged.Raw, cancellationToken);
        }
    }

    public Task<BilibiliApiResponse<TData>> GetJsonAsync<TData>(
        string url,
        CancellationToken cancellationToken,
        IDictionary<string, string?>? query = null,
        string? referer = null,
        string? origin = null,
        bool wbi = false)
    {
        var fullUrl = BuildUrl(url, query, wbi);
        return SendAsync<BilibiliApiResponse<TData>>(CreateRequest(HttpMethod.Get, fullUrl, referer, origin), cancellationToken);
    }

    public Task<BilibiliApiResponse> GetAsync(
        string url,
        CancellationToken cancellationToken,
        IDictionary<string, string?>? query = null,
        string? referer = null,
        string? origin = null,
        bool wbi = false)
    {
        var fullUrl = BuildUrl(url, query, wbi);
        return SendAsync<BilibiliApiResponse>(CreateRequest(HttpMethod.Get, fullUrl, referer, origin), cancellationToken);
    }

    public Task<BilibiliApiResponse<TData>> PostFormAsync<TData>(
        string url,
        IDictionary<string, string?> form,
        CancellationToken cancellationToken,
        string? referer = null,
        string? origin = null)
        => PostCoreAsync<BilibiliApiResponse<TData>>(url, new FormUrlEncodedContent(Normalize(form)), referer, origin, cancellationToken);

    public Task<BilibiliApiResponse> PostFormAsync(
        string url,
        IDictionary<string, string?> form,
        CancellationToken cancellationToken,
        string? referer = null,
        string? origin = null)
        => PostCoreAsync<BilibiliApiResponse>(url, new FormUrlEncodedContent(Normalize(form)), referer, origin, cancellationToken);

    public Task<BilibiliApiResponse<TData>> PostJsonAsync<TData>(
        string url,
        string json,
        CancellationToken cancellationToken,
        string? referer = null,
        string? origin = null)
        => PostCoreAsync<BilibiliApiResponse<TData>>(url, new StringContent(json, Encoding.UTF8, "application/json"), referer, origin, cancellationToken);

    public Task<BilibiliApiResponse> PostJsonAsync(
        string url,
        string json,
        CancellationToken cancellationToken,
        string? referer = null,
        string? origin = null)
        => PostCoreAsync<BilibiliApiResponse>(url, new StringContent(json, Encoding.UTF8, "application/json"), referer, origin, cancellationToken);

    /// <summary>无请求体 POST（部分 B 站接口只认 query 参数）。</summary>
    public Task<BilibiliApiResponse> PostEmptyAsync(
        string url,
        CancellationToken cancellationToken,
        IDictionary<string, string?>? query = null,
        string? referer = null,
        string? origin = null)
    {
        var fullUrl = BuildUrl(url, query);
        return PostCoreAsync<BilibiliApiResponse>(fullUrl, content: null, referer, origin, cancellationToken);
    }

    /// <summary>发送原始请求并返回响应（调用方负责 Dispose），用于扫码登录等需要读取 Set-Cookie 的场景。</summary>
    public Task<HttpResponseMessage> SendRawAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => _http.SendAsync(request, cancellationToken);

    public HttpRequestMessage CreateRequest(HttpMethod method, string url, string? referer = null, string? origin = null)
    {
        var request = new HttpRequestMessage(method, url);
        var cookie = Cookie.Raw;
        if (!string.IsNullOrWhiteSpace(cookie))
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        }

        if (referer is null || origin is null)
        {
            var (defaultReferer, defaultOrigin) = BilibiliEndpoints.ResolveHeaders(url);
            referer ??= defaultReferer;
            origin ??= defaultOrigin;
        }

        request.Headers.TryAddWithoutValidation("Referer", referer);
        request.Headers.TryAddWithoutValidation("Origin", origin);
        return request;
    }

    private string BuildUrl(string url, IDictionary<string, string?>? query, bool wbi = false)
    {
        if (query is null || query.Count == 0)
        {
            return url;
        }

        var queryString = wbi
            ? BilibiliWbiSigner.Sign(query, WbiImgKey, WbiSubKey)
            : string.Join('&', query.Where(p => !string.IsNullOrEmpty(p.Value))
                .Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value!)}"));

        return url.Contains('?') ? $"{url}&{queryString}" : $"{url}?{queryString}";
    }

    private static Dictionary<string, string> Normalize(IDictionary<string, string?> form)
        => form.Where(p => !string.IsNullOrEmpty(p.Value))
            .ToDictionary(p => p.Key, p => p.Value!, StringComparer.Ordinal);

    private Task<TResponse> PostCoreAsync<TResponse>(
        string url, HttpContent? content, string? referer, string? origin, CancellationToken cancellationToken)
        where TResponse : BilibiliApiResponse, new()
    {
        var request = CreateRequest(HttpMethod.Post, url, referer, origin);
        request.Content = content;
        return SendAsync<TResponse>(request, cancellationToken);
    }

    private async Task<TResponse> SendAsync<TResponse>(HttpRequestMessage request, CancellationToken cancellationToken)
        where TResponse : BilibiliApiResponse, new()
    {
        using (request)
        using (var response = await _http.SendAsync(request, cancellationToken))
        {
            var payload = await ReadAsync<TResponse>(response, cancellationToken);
            return payload ?? new TResponse { Code = (int)response.StatusCode, Message = response.ReasonPhrase ?? "空响应" };
        }
    }

    private static async Task<TResponse?> ReadAsync<TResponse>(HttpResponseMessage response, CancellationToken cancellationToken)
        where TResponse : BilibiliApiResponse, new()
    {
        // B 站业务错误常带 JSON body（甚至 HTTP 4xx），不能用 EnsureSuccessStatusCode 直接炸掉整条管线。
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is not null && mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return await response.Content.ReadFromJsonAsync<TResponse>(BilibiliJsonDefaults.Options, cancellationToken);
            }
            catch (JsonException)
            {
                // fall through
            }
        }

        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TResponse>(text, BilibiliJsonDefaults.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
