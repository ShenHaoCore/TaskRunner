using TaskRunner.Bilibili.Models;

namespace TaskRunner.Bilibili.Api;

internal sealed class BilibiliVideoApi(BilibiliHttpClient http) : IBilibiliVideoApi
{
    public Task<BilibiliApiResponse<PopularListData>> GetPopularAsync(int pageSize, CancellationToken cancellationToken)
        => http.GetJsonAsync<PopularListData>(
            BilibiliEndpoints.Popular,
            cancellationToken,
            query: new Dictionary<string, string?> { ["ps"] = pageSize.ToString(), ["pn"] = "1" },
            referer: BilibiliEndpoints.WwwHome,
            origin: BilibiliEndpoints.WwwOrigin);

    public Task<BilibiliApiResponse<VideoViewData>> GetVideoViewAsync(string bvid, long aid, CancellationToken cancellationToken)
        => http.GetJsonAsync<VideoViewData>(
            BilibiliEndpoints.VideoView,
            cancellationToken,
            query: new Dictionary<string, string?> { ["bvid"] = bvid, ["aid"] = aid.ToString() },
            referer: BilibiliEndpoints.WwwHome,
            origin: BilibiliEndpoints.WwwOrigin);

    public Task<BilibiliApiResponse> HeartbeatAsync(long aid, long cid, string bvid, long mid, int playedTime, CancellationToken cancellationToken)
        => http.PostFormAsync(
            BilibiliEndpoints.Heartbeat,
            new Dictionary<string, string?>
            {
                ["aid"] = aid.ToString(),
                ["cid"] = cid.ToString(),
                ["bvid"] = bvid,
                ["mid"] = mid.ToString(),
                ["played_time"] = playedTime.ToString(),
                ["real_played_time"] = playedTime.ToString(),
                ["realtime"] = playedTime.ToString(),
                ["type"] = "3",
                ["dt"] = "2",
                ["play_type"] = "3",
                ["csrf"] = http.Cookie.BilibiliJct
            },
            cancellationToken,
            referer: BilibiliEndpoints.VideoPage(bvid, aid),
            origin: BilibiliEndpoints.WwwOrigin);

    public Task<BilibiliApiResponse> ShareVideoAsync(long aid, string bvid, CancellationToken cancellationToken)
        => http.PostFormAsync(
            BilibiliEndpoints.ShareAdd,
            new Dictionary<string, string?>
            {
                ["aid"] = aid.ToString(),
                ["csrf"] = http.Cookie.BilibiliJct,
                ["eab_x"] = "1",
                ["ramval"] = Random.Shared.Next(3, 20).ToString(),
                ["source"] = "web_normal",
                ["ga"] = "1"
            },
            cancellationToken,
            referer: BilibiliEndpoints.VideoPage(bvid, aid),
            origin: BilibiliEndpoints.WwwOrigin);

    public Task<BilibiliApiResponse> LikeAsync(long aid, string bvid, CancellationToken cancellationToken)
        => http.PostFormAsync(
            BilibiliEndpoints.LikeAdd,
            new Dictionary<string, string?>
            {
                ["aid"] = aid.ToString(),
                ["like"] = "1",
                ["eab_x"] = "2",
                ["ramval"] = "3",
                ["source"] = "web_normal",
                ["ga"] = "1",
                ["csrf"] = http.Cookie.BilibiliJct
            },
            cancellationToken,
            referer: BilibiliEndpoints.VideoPage(bvid, aid),
            origin: BilibiliEndpoints.WwwOrigin);

    public Task<BilibiliApiResponse> AddCoinAsync(long aid, string bvid, int multiply, CancellationToken cancellationToken)
        => http.PostFormAsync(
            BilibiliEndpoints.CoinAdd,
            new Dictionary<string, string?>
            {
                ["aid"] = aid.ToString(),
                ["multiply"] = multiply.ToString(),
                ["select_like"] = "1",
                ["cross_domain"] = "true",
                ["eab_x"] = "2",
                ["ramval"] = "3",
                ["source"] = "web_normal",
                ["ga"] = "1",
                ["csrf"] = http.Cookie.BilibiliJct
            },
            cancellationToken,
            referer: BilibiliEndpoints.VideoPage(bvid, aid),
            origin: BilibiliEndpoints.WwwOrigin);

    public Task<BilibiliApiResponse<UpVideoListData>> GetUpVideosAsync(long upMid, int pageSize, int page, CancellationToken cancellationToken)
        => http.GetJsonAsync<UpVideoListData>(
            BilibiliEndpoints.UpVideosWbi,
            cancellationToken,
            query: new Dictionary<string, string?>
            {
                ["mid"] = upMid.ToString(),
                ["ps"] = pageSize.ToString(),
                ["pn"] = page.ToString(),
                ["order"] = "pubdate",
                ["platform"] = "web",
                ["web_location"] = "333.934"
            },
            referer: BilibiliEndpoints.SpaceHome,
            origin: BilibiliEndpoints.SpaceOrigin,
            wbi: true);
}
