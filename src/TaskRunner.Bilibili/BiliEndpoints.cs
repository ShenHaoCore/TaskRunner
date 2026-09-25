namespace TaskRunner.Bilibili;

/// <summary>
/// B 站各站点地址与 API 端点，集中管理避免硬编码散落。
/// </summary>
internal static class BiliEndpoints
{
    public const string WwwHome = "https://www.bilibili.com/";
    public const string WwwOrigin = "https://www.bilibili.com";

    public const string AccountHome = "https://account.bilibili.com/account/home";
    public const string AccountOrigin = "https://account.bilibili.com";

    public const string MangaHome = "https://manga.bilibili.com/";
    public const string MangaOrigin = "https://manga.bilibili.com";

    public const string LinkHome = "https://link.bilibili.com/";
    public const string LinkOrigin = "https://link.bilibili.com";

    private const string Api = "https://api.bilibili.com";
    private const string LiveApi = "https://api.live.bilibili.com";
    private const string Passport = "https://passport.bilibili.com";

    public const string Nav = Api + "/x/web-interface/nav";
    public const string DailyTask = Api + "/x/member/web/exp/reward";
    public const string Popular = Api + "/x/web-interface/popular?ps=10&pn=1";
    public const string ShareAdd = Api + "/x/web-interface/share/add";
    public const string Heartbeat = Api + "/x/click-interface/web/heartbeat";
    public const string CoinAdd = Api + "/x/web-interface/coin/add";
    public const string LikeAdd = Api + "/x/web-interface/archive/like";
    public const string Followings = Api + "/x/relation/followings";
    public const string UpVideos = Api + "/x/space/arc/search";

    public const string VipPrivilegeList = Api + "/x/vip/privilege/my";
    public const string VipPrivilegeReceive = Api + "/x/vip/privilege/receive";

    public const string ChargeWallet = "https://pay.bilibili.com/paywallet/wb/getUserBalance";
    public const string ChargeQuick = Api + "/x/ugcpay/web/v2/trade/elec/pay/quick";

    public const string MangaClockIn = MangaOrigin + "/twirp/activity.v1.Activity/ClockIn?platform=android";

    public const string LiveSign = LiveApi + "/xlive/web-ucenter/v1/sign/DoSign";
    public const string LiveWalletStatus = LiveApi + "/xlive/revenue/v1/wallet/getStatus";
    public const string Silver2Coin = LiveApi + "/xlive/revenue/v1/wallet/silver2coin";

    public const string QrGenerate = Passport + "/x/passport-login/web/qrcode/generate";
    public const string QrPoll = Passport + "/x/passport-login/web/qrcode/poll";

    /// <summary>
    /// 按目标地址所属站点返回默认的 Referer/Origin。
    /// </summary>
    /// <param name="url"></param>
    /// <returns></returns>
    public static (string Referer, string Origin) ResolveHeaders(string url)
    {
        var host = new Uri(url).Host;
        if (host.EndsWith("manga.bilibili.com", StringComparison.OrdinalIgnoreCase)) { return (MangaHome, MangaOrigin); }
        if (host.EndsWith("live.bilibili.com", StringComparison.OrdinalIgnoreCase)) { return (LinkHome, LinkOrigin); }
        return (WwwHome, WwwOrigin);
    }
}
