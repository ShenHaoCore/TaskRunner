using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TaskRunner.Bilibili.Models;

namespace TaskRunner.Bilibili.Api;

/// <summary>
/// B 站接口统一 HTTP 入口：集中处理 Cookie 注入、Referer/Origin 解析、wbi 签名、
/// 表单/JSON 提交、瞬时错误自动重试，以及「HTTP 4xx 但带业务 JSON」的容错读取。
/// 各领域 API 只声明意图，不直接拼请求。
/// </summary>
public sealed class BilibiliHttpClient(HttpClient http, IBilibiliCookieStore cookieStore)
{
    public const string BrowserUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    /// <summary>额外重试次数（共尝试 3 次）。仅针对瞬时网络错误与 5xx，412/429 风控与业务失败不重试。</summary>
    private const int MaxRetries = 2;

    /// <summary>重试基础间隔（第 n 次重试等待 n × RetryBaseDelay）；测试可设为零。</summary>
    internal TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>当前 Cookie 的只读快照。</summary>
    public BilibiliCookie Cookie => new(cookieStore.GetCookie());

    /// <summary>本次执行的 wbi img 钥匙，Nav 成功后写入；wbi 接口签名时读取。</summary>
    public string? WbiImgKey { get; internal set; }

    /// <summary>本次执行的 wbi sub 钥匙，Nav 成功后写入；wbi 接口签名时读取。</summary>
    public string? WbiSubKey { get; internal set; }

    /// <summary>访问主站补齐 buvid3 等设备 Cookie，并把新 Cookie 落盘。</summary>
    public async Task EnsureBrowserCookiesAsync(CancellationToken cancellationToken)
    {
        var before = Cookie;
        using var response = await SendWithRetryAsync(
            () => CreateRequest(HttpMethod.Get, BilibiliEndpoints.WwwHome, BilibiliEndpoints.WwwHome, BilibiliEndpoints.WwwOrigin),
            cancellationToken);

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
            await cookieStore.SaveAsync(merged.Raw, cancellationToken);
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
        return SendAsync<BilibiliApiResponse<TData>>(() => CreateRequest(HttpMethod.Get, fullUrl, referer, origin), cancellationToken);
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
        return SendAsync<BilibiliApiResponse>(() => CreateRequest(HttpMethod.Get, fullUrl, referer, origin), cancellationToken);
    }

    public Task<BilibiliApiResponse<TData>> PostFormAsync<TData>(
        string url,
        IDictionary<string, string?> form,
        CancellationToken cancellationToken,
        string? referer = null,
        string? origin = null)
        => PostCoreAsync<BilibiliApiResponse<TData>>(url, () => new FormUrlEncodedContent(Normalize(form)), referer, origin, cancellationToken);

    public Task<BilibiliApiResponse> PostFormAsync(
        string url,
        IDictionary<string, string?> form,
        CancellationToken cancellationToken,
        string? referer = null,
        string? origin = null)
        => PostCoreAsync<BilibiliApiResponse>(url, () => new FormUrlEncodedContent(Normalize(form)), referer, origin, cancellationToken);

    public Task<BilibiliApiResponse<TData>> PostJsonAsync<TData>(
        string url,
        string json,
        CancellationToken cancellationToken,
        string? referer = null,
        string? origin = null)
        => PostCoreAsync<BilibiliApiResponse<TData>>(url, () => new StringContent(json, Encoding.UTF8, "application/json"), referer, origin, cancellationToken);

    public Task<BilibiliApiResponse> PostJsonAsync(
        string url,
        string json,
        CancellationToken cancellationToken,
        string? referer = null,
        string? origin = null)
        => PostCoreAsync<BilibiliApiResponse>(url, () => new StringContent(json, Encoding.UTF8, "application/json"), referer, origin, cancellationToken);

    /// <summary>无请求体 POST（部分 B 站接口只认 query 参数）。</summary>
    public Task<BilibiliApiResponse> PostEmptyAsync(
        string url,
        CancellationToken cancellationToken,
        IDictionary<string, string?>? query = null,
        string? referer = null,
        string? origin = null)
    {
        var fullUrl = BuildUrl(url, query);
        return PostCoreAsync<BilibiliApiResponse>(fullUrl, () => null, referer, origin, cancellationToken);
    }

    /// <summary>
    /// 发送原始请求并返回响应（调用方负责 Dispose）。
    /// 不重试 —— 仅用于扫码登录等需要读取 Set-Cookie 的实时交互场景。
    /// </summary>
    internal Task<HttpResponseMessage> SendRawAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => http.SendAsync(request, cancellationToken);

    /// <summary>构造带 Cookie/Referer/Origin 的请求；同程序集内（扫码服务）共享。</summary>
    internal HttpRequestMessage CreateRequest(HttpMethod method, string url, string? referer = null, string? origin = null)
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
        string url, Func<HttpContent?> contentFactory, string? referer, string? origin, CancellationToken cancellationToken)
        where TResponse : BilibiliApiResponse, new()
    {
        return SendAsync<TResponse>(() =>
        {
            var request = CreateRequest(HttpMethod.Post, url, referer, origin);
            request.Content = contentFactory();
            return request;
        }, cancellationToken);
    }

    private async Task<TResponse> SendAsync<TResponse>(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
        where TResponse : BilibiliApiResponse, new()
    {
        using var response = await SendWithRetryAsync(requestFactory, cancellationToken);
        var payload = await ReadAsync<TResponse>(response, cancellationToken);
        return payload ?? new TResponse { Code = (int)response.StatusCode, Message = response.ReasonPhrase ?? "空响应" };
    }

    /// <summary>
    /// 发送请求，瞬时网络错误（连接异常/超时）与 5xx 自动重试，退避 1s→2s；
    /// 412/429 风控与 4xx 不重试（业务 JSON 由 <see cref="ReadAsync{TResponse}"/> 容错解析）。
    /// 每次重试都重建请求（HttpRequestMessage/Content 不可复用）。
    /// </summary>
    private async Task<HttpResponseMessage> SendWithRetryAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var response = await http.SendAsync(requestFactory(), cancellationToken);
                if ((int)response.StatusCode >= 500 && attempt < MaxRetries)
                {
                    response.Dispose();
                    await Task.Delay(RetryBaseDelay * (attempt + 1), cancellationToken);
                    continue;
                }

                return response;
            }
            catch (HttpRequestException) when (attempt < MaxRetries)
            {
                await Task.Delay(RetryBaseDelay * (attempt + 1), cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < MaxRetries)
            {
                await Task.Delay(RetryBaseDelay * (attempt + 1), cancellationToken);
            }
        }
    }

    /// <summary>
    /// B 站业务错误常带 JSON body（甚至 HTTP 4xx），不能用 EnsureSuccessStatusCode 直接炸掉整条管线。
    /// </summary>
    private static async Task<TResponse?> ReadAsync<TResponse>(HttpResponseMessage response, CancellationToken cancellationToken)
        where TResponse : BilibiliApiResponse, new()
    {
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
