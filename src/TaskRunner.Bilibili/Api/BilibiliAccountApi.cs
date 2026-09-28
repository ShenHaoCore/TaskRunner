using TaskRunner.Bilibili.Models;

namespace TaskRunner.Bilibili.Api;

/// <summary>
/// 
/// </summary>
/// <param name="http"></param>
internal sealed class BilibiliAccountApi(BilibiliHttpClient http) : IBilibiliAccountApi
{
    /// <summary>
    /// 
    /// </summary>
    public BilibiliCookie Cookie => http.Cookie;

    /// <summary>
    /// 
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task EnsureBrowserCookiesAsync(CancellationToken cancellationToken)
        => http.EnsureBrowserCookiesAsync(cancellationToken);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<BilibiliApiResponse<NavData>> GetNavAsync(CancellationToken cancellationToken)
    {
        var nav = await http.GetJsonAsync<NavData>(BilibiliEndpoints.Nav, cancellationToken, referer: BilibiliEndpoints.WwwHome, origin: BilibiliEndpoints.WwwOrigin);

        if (nav.IsSuccess && nav.Data?.WbiImg is { } wbi)
        {
            http.WbiImgKey = wbi.ImgUrl;
            http.WbiSubKey = wbi.SubUrl;
        }

        return nav;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="selfMid"></param>
    /// <param name="pageSize"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<BilibiliApiResponse<FollowingListData>> GetFollowingsAsync(long selfMid, int pageSize, CancellationToken cancellationToken)
        => http.GetJsonAsync<FollowingListData>(BilibiliEndpoints.Followings, cancellationToken, query: new Dictionary<string, string?>
        {
            ["vmid"] = selfMid.ToString(),
            ["pn"] = "1",
            ["ps"] = pageSize.ToString(),
            ["order"] = "desc",
            ["order_type"] = "attention"
        }, referer: BilibiliEndpoints.SpaceHome, origin: BilibiliEndpoints.SpaceOrigin);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<BilibiliApiResponse<DailyTaskInfo>> GetDailyTaskAsync(CancellationToken cancellationToken)
        => http.GetJsonAsync<DailyTaskInfo>(BilibiliEndpoints.DailyTask, cancellationToken, referer: BilibiliEndpoints.AccountHome, origin: BilibiliEndpoints.AccountOrigin);
}
