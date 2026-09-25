using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TaskRunner.Bilibili.Models;

namespace TaskRunner.Bilibili;

public sealed class BiliApiClient
{
    public const string BrowserUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

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
        using var request = CreateRequest(HttpMethod.Get, BiliEndpoints.WwwHome, BiliEndpoints.WwwHome, BiliEndpoints.WwwOrigin);
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
        GetAsync<NavData>(BiliEndpoints.Nav, cancellationToken);

    public Task<BiliApiResponse<DailyTaskInfo>> GetDailyTaskAsync(CancellationToken cancellationToken) =>
        GetAsync<DailyTaskInfo>(
            BiliEndpoints.DailyTask,
            cancellationToken,
            referer: BiliEndpoints.AccountHome,
            origin: BiliEndpoints.AccountOrigin);

    public Task<BiliApiResponse<PopularListData>> GetPopularAsync(CancellationToken cancellationToken) =>
        GetAsync<PopularListData>(BiliEndpoints.Popular, cancellationToken);

    public Task<BiliApiResponse<FollowingListData>> GetFollowingsAsync(long vmid, CancellationToken cancellationToken) =>
        GetAsync<FollowingListData>($"{BiliEndpoints.Followings}?vmid={vmid}&pn=1&ps=50", cancellationToken);

    public Task<BiliApiResponse<UpVideoListData>> GetUpVideosAsync(long mid, int pn, CancellationToken cancellationToken) =>
        GetAsync<UpVideoListData>($"{BiliEndpoints.UpVideos}?mid={mid}&ps=30&pn={pn}", cancellationToken);

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
        return await PostAsync(BiliEndpoints.ShareAdd, content, cancellationToken);
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
        var url = $"{BiliEndpoints.Heartbeat}?aid={aid}&played_time={playedTime}";
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
        return await PostAsync(BiliEndpoints.CoinAdd, content, cancellationToken);
    }

    public async Task<BiliApiResponse> MangaClockInAsync(CancellationToken cancellationToken)
    {
        // 漫画站要求 Origin/Referer 为 manga.bilibili.com；空 JSON 体比无 Content 更稳妥。
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        return await PostAsync(BiliEndpoints.MangaClockIn, content, cancellationToken);
    }

    public Task<BiliApiResponse> LiveSignAsync(CancellationToken cancellationToken) =>
        GetAsResponseAsync(BiliEndpoints.LiveSign, cancellationToken);

    public Task<BiliApiResponse<LiveWalletStatus>> GetLiveWalletStatusAsync(CancellationToken cancellationToken) =>
        GetAsync<LiveWalletStatus>(BiliEndpoints.LiveWalletStatus, cancellationToken);

    public async Task<BiliApiResponse> Silver2CoinAsync(CancellationToken cancellationToken)
    {
        var csrf = RequireCsrf();
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["csrf"] = csrf,
            ["csrf_token"] = csrf
        });
        return await PostAsync(BiliEndpoints.Silver2Coin, content, cancellationToken);
    }

    public async Task<BiliApiResponse> LikeAsync(long aid, CancellationToken cancellationToken)
    {
        var csrf = RequireCsrf();
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["aid"] = aid.ToString(),
            ["like"] = "1",
            ["csrf"] = csrf
        });
        return await PostAsync(BiliEndpoints.LikeAdd, content, cancellationToken);
    }

    public Task<BiliApiResponse<VipPrivilegeList>> GetVipPrivilegesAsync(CancellationToken cancellationToken) =>
        GetAsync<VipPrivilegeList>(BiliEndpoints.VipPrivilegeList, cancellationToken);

    public async Task<BiliApiResponse> ReceiveVipPrivilegeAsync(int type, CancellationToken cancellationToken)
    {
        var csrf = RequireCsrf();
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["type"] = type.ToString(),
            ["csrf"] = csrf
        });
        return await PostAsync(BiliEndpoints.VipPrivilegeReceive, content, cancellationToken);
    }

    public Task<BiliApiResponse<ChargeWalletData>> GetChargeWalletAsync(CancellationToken cancellationToken) =>
        GetAsync<ChargeWalletData>(BiliEndpoints.ChargeWallet, cancellationToken);

    public async Task<BiliApiResponse> ChargeQuickAsync(long mid, decimal num, CancellationToken cancellationToken)
    {
        var csrf = RequireCsrf();
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["bp_num"] = num.ToString("0"),
            ["is_bp_remains_prior"] = "true",
            ["up_mid"] = mid.ToString(),
            ["otype"] = "up",
            ["oid"] = mid.ToString(),
            ["csrf"] = csrf
        });
        return await PostAsync(BiliEndpoints.ChargeQuick, content, cancellationToken);
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

    private HttpRequestMessage CreateRequest(HttpMethod method, string url, string? referer = null, string? origin = null)
    {
        var request = new HttpRequestMessage(method, url);
        var cookie = Cookie.Raw;
        if (!string.IsNullOrWhiteSpace(cookie))
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        }

        if (referer is null || origin is null)
        {
            var (defaultReferer, defaultOrigin) = BiliEndpoints.ResolveHeaders(url);
            referer ??= defaultReferer;
            origin ??= defaultOrigin;
        }

        request.Headers.TryAddWithoutValidation("Referer", referer);
        request.Headers.TryAddWithoutValidation("Origin", origin);
        return request;
    }
}
