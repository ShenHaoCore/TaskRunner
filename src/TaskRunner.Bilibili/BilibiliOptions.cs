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
}
