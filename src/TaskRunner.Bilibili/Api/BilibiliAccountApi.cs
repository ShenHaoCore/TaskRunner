using TaskRunner.Bilibili.Models;

namespace TaskRunner.Bilibili.Api;

internal sealed class BilibiliAccountApi(BilibiliHttpClient http) : IBilibiliAccountApi
{
    public BilibiliCookie Cookie => http.Cookie;

    public Task EnsureBrowserCookiesAsync(CancellationToken cancellationToken)
        => http.EnsureBrowserCookiesAsync(cancellationToken);

    public async Task<BilibiliApiResponse<NavData>> GetNavAsync(CancellationToken cancellationToken)
    {
        var nav = await http.GetJsonAsync<NavData>(
            BilibiliEndpoints.Nav,
            cancellationToken,
            referer: BilibiliEndpoints.WwwHome,
            origin: BilibiliEndpoints.WwwOrigin);

        if (nav.IsSuccess && nav.Data?.WbiImg is { } wbi)
        {
            http.WbiImgKey = wbi.ImgUrl;
            http.WbiSubKey = wbi.SubUrl;
        }

        return nav;
    }

    public Task<BilibiliApiResponse<FollowingListData>> GetFollowingsAsync(long selfMid, int pageSize, CancellationToken cancellationToken)
        => http.GetJsonAsync<FollowingListData>(
            BilibiliEndpoints.Followings,
            cancellationToken,
            query: new Dictionary<string, string?>
            {
                ["vmid"] = selfMid.ToString(),
                ["pn"] = "1",
                ["ps"] = pageSize.ToString(),
                ["order"] = "desc",
                ["order_type"] = "attention"
            },
            referer: BilibiliEndpoints.SpaceHome,
            origin: BilibiliEndpoints.SpaceOrigin);

    public Task<BilibiliApiResponse<DailyTaskInfo>> GetDailyTaskAsync(CancellationToken cancellationToken)
        => http.GetJsonAsync<DailyTaskInfo>(
            BilibiliEndpoints.DailyTask,
            cancellationToken,
            referer: BilibiliEndpoints.AccountHome,
            origin: BilibiliEndpoints.AccountOrigin);
}
