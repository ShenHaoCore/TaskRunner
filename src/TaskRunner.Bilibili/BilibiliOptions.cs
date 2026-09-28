namespace TaskRunner.Bilibili;

public sealed class BilibiliOptions
{
    public const string SectionName = "Bilibili";

    /// <summary>浏览器登录 Cookie，至少包含 SESSDATA 与 bili_jct。扫码落盘文件优先于本配置。</summary>
    public string Cookie { get; set; } = string.Empty;

    /// <summary>可选。Cookie 文件路径（相对仓库根/共享目录或绝对路径）。未配置时开发用仓库 data/，发布用 LocalApplicationData/TaskRunner。</summary>
    public string? CookieFile { get; set; }

    /// <summary>每日投币数量，0 表示跳过投币。</summary>
    public int NumberOfCoins { get; set; }

    public bool EnableMangaSign { get; set; } = true;

    public bool EnableLiveSign { get; set; } = true;

    public bool EnableSilver2Coin { get; set; } = true;

    /// <summary>每日观看视频后点赞（独立于投币的 select_like）。</summary>
    public bool EnableLike { get; set; } = true;

    /// <summary>每日幂等领取大会员 B 币券与漫画福利券（仅年度大会员，已领取的自动跳过）。</summary>
    public bool EnableVipPrivilege { get; set; } = true;

    /// <summary>有 B 币券余额即自动充电（每日检查，仅年度大会员，充电后余额归零）。</summary>
    public bool EnableCharge { get; set; } = true;

    /// <summary>充电目标 UP 主 mid。必须指定他人：B 站已禁止给自己充电；0 时使用兜底官方账号。</summary>
    public long ChargeUpMid { get; set; }
}
