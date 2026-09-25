using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskRunner.Core.Jobs.Progress;

namespace TaskRunner.Bilibili;

public interface IBilibiliDailyPipeline
{
    Task RunAsync(IJobProgress progress, CancellationToken cancellationToken);
}

public sealed class BilibiliDailyPipeline(
    BiliApiClient client,
    IOptions<BilibiliOptions> options,
    ILogger<BilibiliDailyPipeline> logger) : IBilibiliDailyPipeline
{
    // 常见「已完成」类错误码，不中断管线
    private static readonly int[] SkippableCodes = [71000, 1011040, 34005, -104];

    public async Task RunAsync(IJobProgress progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var stopwatch = Stopwatch.StartNew();
        var settings = options.Value;

        progress.WriteLine(">>> BILIBILI 每日协议 v2.4 <<<");
        progress.SetProgress(0);

        var nav = await PrepareAsync(progress, cancellationToken);
        var info = await ScanDailyStatusAsync(progress, cancellationToken);
        var video = await PickVideoAsync(cancellationToken);
        progress.WriteLine($"[目标] {video.Bvid}《{video.Title}》(aid={video.Aid})");
        progress.SetProgress(30);

        progress.WriteLine("=== 执行阶段 ===");
        await SyncWatchShareAsync(progress, video, nav, info, cancellationToken);
        await ThrowCoinsAsync(progress, settings, video, info, cancellationToken);
        await SignMangaAsync(progress, settings, cancellationToken);
        await SignLiveAsync(progress, settings, cancellationToken);
        await ExchangeSilverAsync(progress, settings, cancellationToken);

        progress.SetProgress(100);
        progress.WriteLine($">>> 协议执行完毕 | 耗时 {stopwatch.Elapsed.TotalSeconds:F1} 秒 <<<");
    }

    /// <summary>准备：校验登录 Cookie、补齐设备指纹、确认登录态。</summary>
    private async Task<Models.NavData> PrepareAsync(IJobProgress progress, CancellationToken cancellationToken)
    {
        if (!client.Cookie.HasLoginTokens)
        {
            throw new InvalidOperationException(
                "未配置有效 Cookie（需要 SESSDATA 与 bili_jct）。请扫码登录 POST /api/bilibili/login/qrcode，或设置 Bilibili:Cookie / data/bilibili-cookie.txt。");
        }

        await client.EnsureBrowserCookiesAsync(cancellationToken);
        progress.WriteLine(client.Cookie.HasBuvid
            ? "[认证] 设备指纹已注入（buvid）"
            : "[认证] 警告：设备指纹缺失 buvid3，部分链路可能触发风控");
        progress.SetProgress(5);

        var nav = await client.GetNavAsync(cancellationToken);
        EnsureSuccess(nav, "登录校验");
        if (nav.Data is not { IsLogin: true })
        {
            throw new InvalidOperationException("Cookie 无效或已过期，导航接口显示未登录。");
        }

        progress.WriteLine($"[身份] {BiliNameMask.Mask(nav.Data.Uname)} | UID={nav.Data.Mid} | 硬币={nav.Data.Money}");
        progress.SetProgress(10);
        return nav.Data;
    }

    /// <summary>扫描每日任务完成状态；失败时按未完成继续。</summary>
    private async Task<Models.DailyTaskInfo> ScanDailyStatusAsync(IJobProgress progress, CancellationToken cancellationToken)
    {
        var daily = await client.GetDailyTaskAsync(cancellationToken);
        if (!daily.IsSuccess)
        {
            progress.WriteLine($"[扫描] 任务扫描异常 code={daily.Code} {daily.DisplayMessage}（继续执行）");
            logger.LogWarning("读取每日任务状态失败：code={Code} {Message}", daily.Code, daily.DisplayMessage);
            progress.SetProgress(20);
            return new Models.DailyTaskInfo();
        }

        var info = daily.Data ?? new Models.DailyTaskInfo();
        progress.WriteLine($"[扫描] 登录[{Status(info.Login)}] 观看[{Status(info.Watch)}] 分享[{Status(info.Share)}] 投币经验={info.Coins}");
        progress.SetProgress(20);
        return info;
    }

    private async Task SyncWatchShareAsync(
        IJobProgress progress, Models.PopularVideo video, Models.NavData nav, Models.DailyTaskInfo info, CancellationToken cancellationToken)
    {
        if (!info.Watch)
        {
            var hb = await client.HeartbeatAsync(video.Aid, video.Cid, video.Bvid ?? string.Empty, nav.Mid, playedTime: 15, cancellationToken);
            LogStep(progress, "观看上报", hb);
        }
        else
        {
            progress.WriteLine("[观看上报] 跳过（今日已同步）");
        }
        progress.SetProgress(45);

        if (!info.Share)
        {
            var share = await client.ShareVideoAsync(video.Aid, cancellationToken);
            LogStep(progress, "分享视频", share);
        }
        else
        {
            progress.WriteLine("[分享视频] 跳过（今日已同步）");
        }
        progress.SetProgress(55);
    }

    private async Task ThrowCoinsAsync(
        IJobProgress progress, BilibiliOptions settings, Models.PopularVideo video, Models.DailyTaskInfo info, CancellationToken cancellationToken)
    {
        var quota = Math.Clamp(settings.NumberOfCoins, 0, 5);
        if (quota <= 0)
        {
            progress.WriteLine("[投币任务] 跳过（目标=0）");
            progress.SetProgress(70);
            return;
        }

        var already = (int)Math.Min(info.Coins / 10, 5);
        var need = Math.Max(0, quota - already);
        progress.WriteLine($"[投币任务] 目标={quota} | 已获={already} | 还需={need}");
        for (var i = 0; i < need; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var coin = await client.AddCoinAsync(video.Aid, multiply: 1, cancellationToken);
            LogStep(progress, $"投币 {i + 1}/{need}", coin);
            if (!coin.IsSuccess && coin.Code is not 34005 and not -104)
            {
                break;
            }
        }
        progress.SetProgress(70);
    }

    private async Task SignMangaAsync(IJobProgress progress, BilibiliOptions settings, CancellationToken cancellationToken)
    {
        if (!settings.EnableMangaSign)
        {
            progress.WriteLine("[漫画节点] 离线（配置已关闭）");
            progress.SetProgress(80);
            return;
        }

        try
        {
            var manga = await client.MangaClockInAsync(cancellationToken);
            // 重复签到常见 code=1 / "already clock in"
            if (manga.IsSuccess || manga.Code is 1 or -1)
            {
                progress.WriteLine($"[漫画节点] 跳过 code={manga.Code} {manga.DisplayMessage}");
            }
            else
            {
                logger.LogWarning("漫画签到失败：code={Code} {Message}", manga.Code, manga.DisplayMessage);
                progress.WriteLine($"[漫画节点] 失败 code={manga.Code} {manga.DisplayMessage}");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "漫画签到异常，已跳过。");
            progress.WriteLine($"[漫画节点] 异常：{ex.Message}");
        }
        progress.SetProgress(80);
    }

    private async Task SignLiveAsync(IJobProgress progress, BilibiliOptions settings, CancellationToken cancellationToken)
    {
        if (settings.EnableLiveSign)
        {
            var live = await client.LiveSignAsync(cancellationToken);
            LogStep(progress, "直播节点", live);
        }
        else
        {
            progress.WriteLine("[直播节点] 离线（配置已关闭）");
        }
        progress.SetProgress(90);
    }

    private async Task ExchangeSilverAsync(IJobProgress progress, BilibiliOptions settings, CancellationToken cancellationToken)
    {
        if (!settings.EnableSilver2Coin)
        {
            progress.WriteLine("[银瓜子兑换] 离线（配置已关闭）");
            return;
        }

        var wallet = await client.GetLiveWalletStatusAsync(cancellationToken);
        if (!wallet.IsSuccess || wallet.Data is null)
        {
            progress.WriteLine($"[钱包扫描] 读取失败 code={wallet.Code} {wallet.DisplayMessage}");
            return;
        }

        progress.WriteLine($"[钱包扫描] 银瓜子={wallet.Data.Silver} | 今日可兑换={wallet.Data.Silver2CoinLeft}");
        if (wallet.Data.Silver2CoinLeft > 0 && wallet.Data.Silver >= 700)
        {
            var exchange = await client.Silver2CoinAsync(cancellationToken);
            LogStep(progress, "银瓜子兑换", exchange);
        }
        else
        {
            progress.WriteLine("[银瓜子兑换] 跳过（银瓜子不足或今日已兑换）");
        }
    }

    private async Task<Models.PopularVideo> PickVideoAsync(CancellationToken cancellationToken)
    {
        var popular = await client.GetPopularAsync(cancellationToken);
        EnsureSuccess(popular, "获取热门视频");
        var video = popular.Data?.List?.FirstOrDefault(item => item.Aid > 0 && !string.IsNullOrWhiteSpace(item.Bvid));
        return video ?? throw new InvalidOperationException("热门列表为空，无法选取视频。");
    }

    private void LogStep(IJobProgress progress, string sector, Models.BiliApiResponse response)
    {
        if (response.IsSuccess)
        {
            progress.WriteLine($"[{sector}] 同步完成");
            return;
        }

        if (SkippableCodes.Contains(response.Code))
        {
            progress.WriteLine($"[{sector}] 跳过 code={response.Code} {response.DisplayMessage}");
            logger.LogInformation("{Sector} 已跳过：{Code} {Message}", sector, response.Code, response.DisplayMessage);
            return;
        }

        progress.WriteLine($"[{sector}] 失败 code={response.Code} {response.DisplayMessage}");
        logger.LogWarning("{Sector} 失败：{Code} {Message}", sector, response.Code, response.DisplayMessage);
    }

    private static void EnsureSuccess(Models.BiliApiResponse response, string step)
    {
        if (!response.IsSuccess)
        {
            throw new InvalidOperationException($"{step}失败：code={response.Code} {response.DisplayMessage}");
        }
    }

    private static string Status(bool ok) => ok ? "已完成" : "待执行";
}
