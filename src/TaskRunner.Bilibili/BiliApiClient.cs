using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TaskRunner.Bilibili.Models;

namespace TaskRunner.Bilibili;

public sealed class BiliApiClient
{
    public const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    private readonly HttpClient _http;
    private readonly IBilibiliCookieStore _cookieStore;

    public BiliApiClient(HttpClient http, IBilibiliCookieStore cookieStore)
    {
        _http = http;
        _cookieStore = cookieStore;
    }

    public BiliCookie Cookie => new(_cookieStore.GetCookie());

    /// <summary>
    /// 访问主站补齐 buvid3 等设备 Cookie。缺少时部分接口会返回 HTTP 412。
    /// </summary>
    public async Task EnsureBrowserCookiesAsync(CancellationToken cancellationToken)
    {
        var before = Cookie;
        using var request = CreateRequest(HttpMethod.Get, "https://www.bilibili.com/", "https://www.bilibili.com/", "https://www.bilibili.com");
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

        if (merged.HasLoginTokens)
        {
            await _cookieStore.SaveAsync(merged.Raw, cancellationToken);
        }
    }

    public Task<BiliApiResponse<NavData>> GetNavAsync(CancellationToken cancellationToken) =>
        GetAsync<NavData>("https://api.bilibili.com/x/web-interface/nav", cancellationToken);

    public Task<BiliApiResponse<DailyTaskInfo>> GetDailyTaskAsync(CancellationToken cancellationToken) =>
        GetAsync<DailyTaskInfo>(
            "https://api.bilibili.com/x/member/web/exp/reward",
            cancellationToken,
            referer: "https://account.bilibili.com/account/home",
            origin: "https://account.bilibili.com");

    public Task<BiliApiResponse<PopularListData>> GetPopularAsync(CancellationToken cancellationToken) =>
        GetAsync<PopularListData>("https://api.bilibili.com/x/web-interface/popular?ps=10&pn=1", cancellationToken);

    public async Task<BiliApiResponse> ShareVideoAsync(long aid, CancellationToken cancellationToken)
    {
        var csrf = RequireCsrf();
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["aid"] = aid.ToString(),
            ["csrf"] = csrf,
            ["eab_x"] = "1",
            ["source"] = "web_normal",
            ["ga"] = "1"
        });
        return await PostAsync("https://api.bilibili.com/x/web-interface/share/add", content, cancellationToken);
    }

    public async Task<BiliApiResponse> HeartbeatAsync(
        long aid,
        long cid,
        string bvid,
        long mid,
        int playedTime,
        CancellationToken cancellationToken)
    {
        var csrf = RequireCsrf();
        var startTs = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["aid"] = aid.ToString(),
            ["cid"] = cid.ToString(),
            ["bvid"] = bvid,
            ["mid"] = mid.ToString(),
            ["csrf"] = csrf,
            ["played_time"] = playedTime.ToString(),
            ["real_played_time"] = playedTime.ToString(),
            ["realtime"] = playedTime.ToString(),
            ["start_ts"] = startTs.ToString(),
            ["type"] = "3",
            ["dt"] = "2",
            ["play_type"] = "3"
        });
        var url = $"https://api.bilibili.com/x/click-interface/web/heartbeat?aid={aid}&played_time={playedTime}";
        return await PostAsync(url, content, cancellationToken);
    }

    public async Task<BiliApiResponse> AddCoinAsync(long aid, int multiply, CancellationToken cancellationToken)
    {
        var csrf = RequireCsrf();
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["aid"] = aid.ToString(),
            ["multiply"] = multiply.ToString(),
            ["select_like"] = "1",
            ["cross_domain"] = "true",
            ["csrf"] = csrf
        });
        return await PostAsync("https://api.bilibili.com/x/web-interface/coin/add", content, cancellationToken);
    }

    public async Task<BiliApiResponse> MangaClockInAsync(CancellationToken cancellationToken)
    {
        // 漫画站要求 Origin/Referer 为 manga.bilibili.com；空 JSON 体比无 Content 更稳妥。
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        return await PostAsync(
            "https://manga.bilibili.com/twirp/activity.v1.Activity/ClockIn?platform=android",
            content,
            cancellationToken);
    }

    public Task<BiliApiResponse> LiveSignAsync(CancellationToken cancellationToken) =>
        GetAsResponseAsync("https://api.live.bilibili.com/xlive/web-ucenter/v1/sign/DoSign", cancellationToken);

    public Task<BiliApiResponse<LiveWalletStatus>> GetLiveWalletStatusAsync(CancellationToken cancellationToken) =>
        GetAsync<LiveWalletStatus>("https://api.live.bilibili.com/xlive/revenue/v1/wallet/getStatus", cancellationToken);

    public async Task<BiliApiResponse> Silver2CoinAsync(CancellationToken cancellationToken)
    {
        var csrf = RequireCsrf();
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["csrf"] = csrf,
            ["csrf_token"] = csrf
        });
        return await PostAsync("https://api.live.bilibili.com/xlive/revenue/v1/wallet/silver2coin", content, cancellationToken);
    }

    private string RequireCsrf()
    {
        var csrf = Cookie.BiliJct;
        if (string.IsNullOrWhiteSpace(csrf))
        {
            throw new InvalidOperationException("Cookie 缺少 bili_jct。");
        }

        return csrf;
    }

    private async Task<BiliApiResponse<T>> GetAsync<T>(
        string url,
        CancellationToken cancellationToken,
        string? referer = null,
        string? origin = null)
    {
        using var request = CreateRequest(HttpMethod.Get, url, referer, origin);
        using var response = await _http.SendAsync(request, cancellationToken);
        return await ReadAsync<BiliApiResponse<T>>(response, cancellationToken)
               ?? new BiliApiResponse<T> { Code = (int)response.StatusCode, Message = response.ReasonPhrase ?? "空响应" };
    }

    private async Task<BiliApiResponse> GetAsResponseAsync(string url, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, cancellationToken);
        return await ReadAsync<BiliApiResponse>(response, cancellationToken)
               ?? new BiliApiResponse { Code = (int)response.StatusCode, Message = response.ReasonPhrase ?? "空响应" };
    }

    private async Task<BiliApiResponse> PostAsync(string url, HttpContent content, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, url);
        request.Content = content;
        using var response = await _http.SendAsync(request, cancellationToken);
        return await ReadAsync<BiliApiResponse>(response, cancellationToken)
               ?? new BiliApiResponse { Code = (int)response.StatusCode, Message = response.ReasonPhrase ?? "空响应" };
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        // B 站业务错误常带 JSON body（甚至 HTTP 4xx），不能用 EnsureSuccessStatusCode 直接炸掉整条管线。
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is not null && mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return await response.Content.ReadFromJsonAsync<T>(BiliJson.Options, cancellationToken);
            }
            catch (JsonException)
            {
                // fall through
            }
        }

        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(text))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(text, BiliJson.Options);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private HttpRequestMessage CreateRequest(
        HttpMethod method,
        string url,
        string? referer = null,
        string? origin = null)
    {
        var request = new HttpRequestMessage(method, url);
        var cookie = Cookie.Raw;
        if (!string.IsNullOrWhiteSpace(cookie))
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        }

        if (referer is null || origin is null)
        {
            var host = new Uri(url).Host;
            if (host.Contains("manga.bilibili.com", StringComparison.OrdinalIgnoreCase))
            {
                referer ??= "https://manga.bilibili.com/";
                origin ??= "https://manga.bilibili.com";
            }
            else if (host.Contains("live.bilibili.com", StringComparison.OrdinalIgnoreCase))
            {
                referer ??= "https://link.bilibili.com/";
                origin ??= "https://link.bilibili.com";
            }
            else
            {
                referer ??= "https://www.bilibili.com/";
                origin ??= "https://www.bilibili.com";
            }
        }

        request.Headers.TryAddWithoutValidation("Referer", referer);
        request.Headers.TryAddWithoutValidation("Origin", origin);
        return request;
    }
}
