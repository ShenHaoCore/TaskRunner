namespace TaskRunner.Bilibili;

/// <summary>
/// B 站各站点地址与 API 端点，集中管理避免硬编码散落。
/// </summary>
internal static class BilibiliEndpoints
{
    public const string WwwHome = "https://www.bilibili.com/";
    public const string WwwOrigin = "https://www.bilibili.com";

    public const string AccountHome = "https://account.bilibili.com/account/home";
    public const string AccountOrigin = "https://account.bilibili.com";

    public const string SpaceHome = "https://space.bilibili.com/";
    public const string SpaceOrigin = "https://space.bilibili.com";

    public const string MangaHome = "https://manga.bilibili.com/";
    public const string MangaOrigin = "https://manga.bilibili.com";

    public const string LinkHome = "https://link.bilibili.com/";
    public const string LinkOrigin = "https://link.bilibili.com";

    /// <summary>大会员大积分任务页，权益相关接口必须以它为 Referer。</summary>
    public const string BigPointPage = "https://big.bilibili.com/mobile/bigPoint/task";

    private const string Api = "https://api.bilibili.com";
    private const string LiveApi = "https://api.live.bilibili.com";
    private const string Passport = "https://passport.bilibili.com";

    // 账号 / 每日经验
    public const string Nav = Api + "/x/web-interface/nav";
    public const string DailyTask = Api + "/x/member/web/exp/reward";

    // 视频
    public const string Popular = Api + "/x/web-interface/popular";
    public const string VideoView = Api + "/x/web-interface/view";
    public const string ShareAdd = Api + "/x/web-interface/share/add";
    public const string Heartbeat = Api + "/x/click-interface/web/heartbeat";
    public const string CoinAdd = Api + "/x/web-interface/coin/add";
    public const string LikeAdd = Api + "/x/web-interface/archive/like";

    // 关系 / 空间（空间接口强制 wbi 签名）
    public const string Followings = Api + "/x/relation/followings";
    public const string UpVideosWbi = Api + "/x/space/wbi/arc/search";

    // 大会员权益
    public const string VipPrivilegeList = Api + "/x/vip/privilege/my";
    public const string VipPrivilegeReceive = Api + "/x/vip/privilege/receive";

    // 充电 V2（B 币券余额从 nav.wallet 读取，无独立钱包接口）
    public const string ChargeQuickV2 = Api + "/x/ugcpay/web/v2/trade/elec/pay/quick";

    // 漫画
    public const string MangaClockIn = MangaOrigin + "/twirp/activity.v1.Activity/ClockIn?platform=android";

    // 直播
    public const string LiveSign = LiveApi + "/xlive/web-ucenter/v1/sign/DoSign";
    public const string LiveWalletStatus = LiveApi + "/xlive/revenue/v1/wallet/getStatus";
    public const string Silver2Coin = LiveApi + "/xlive/revenue/v1/wallet/silver2coin";

    // 扫码登录
    public const string QrGenerate = Passport + "/x/passport-login/web/qrcode/generate";
    public const string QrPoll = Passport + "/x/passport-login/web/qrcode/poll";

    /// <summary>构造视频播放页地址，投币/点赞/分享等写操作需要该页面作为 Referer 以通过风控。</summary>
    public static string VideoPage(string? bvid, long aid) =>
        string.IsNullOrWhiteSpace(bvid)
            ? $"{WwwOrigin}/video/av{aid}"
            : $"{WwwOrigin}/video/{bvid}";

    /// <summary>
    /// 按目标地址所属站点返回默认的 Referer/Origin。
    /// </summary>
    public static (string Referer, string Origin) ResolveHeaders(string url)
    {
        var host = new Uri(url).Host;
        if (host.EndsWith("manga.bilibili.com", StringComparison.OrdinalIgnoreCase))
        {
            return (MangaHome, MangaOrigin);
        }

        if (host.EndsWith("live.bilibili.com", StringComparison.OrdinalIgnoreCase))
        {
            return (LinkHome, LinkOrigin);
        }

        return (WwwHome, WwwOrigin);
    }
}
