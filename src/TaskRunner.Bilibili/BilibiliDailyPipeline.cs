using Microsoft.Extensions.Logging;
using System.Diagnostics;
using TaskRunner.Bilibili.Api;
using TaskRunner.Bilibili.Models;
using TaskRunner.Bilibili.Tasks;
using TaskRunner.Core.Jobs.Progress;

namespace TaskRunner.Bilibili;

public interface IBilibiliDailyPipeline
{
    Task RunAsync(IJobProgress progress, CancellationToken cancellationToken);
}

public sealed class BilibiliDailyPipeline(
    IBilibiliAccountApi account,
    IWatchShareTask watchShareTask,
    ILikeTask likeTask,
    ICoinTask coinTask,
    IMangaSignTask mangaSignTask,
    ILiveSignTask liveSignTask,
    ISilverTask silverTask,
    IVipPrivilegeTask vipPrivilegeTask,
    IChargeTask chargeTask,
    ILogger<BilibiliDailyPipeline> logger) : IBilibiliDailyPipeline
{
    public async Task RunAsync(IJobProgress progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var stopwatch = Stopwatch.StartNew();

        progress.WriteLine(">>> BILIBILI 每日协议 v2.7 <<<");
        progress.SetProgress(0);

        var nav = await PrepareAsync(progress, cancellationToken);
        var info = await ScanDailyStatusAsync(progress, cancellationToken);

        progress.WriteLine("=== 每日阶段 ===");
        await watchShareTask.ExecuteAsync(progress, nav, info, cancellationToken);
        progress.SetProgress(50);

        await likeTask.ExecuteAsync(progress, nav, info, cancellationToken);
        progress.SetProgress(60);

        await coinTask.ExecuteAsync(progress, nav, info, cancellationToken);
        progress.SetProgress(70);

        await mangaSignTask.ExecuteAsync(progress, cancellationToken);
        progress.SetProgress(80);

        await liveSignTask.ExecuteAsync(progress, cancellationToken);
        progress.SetProgress(85);

        await silverTask.ExecuteAsync(progress, cancellationToken);
        progress.SetProgress(90);

        await vipPrivilegeTask.ExecuteAsync(progress, nav, cancellationToken);
        progress.SetProgress(95);

        await chargeTask.ExecuteAsync(progress, nav, cancellationToken);

        progress.SetProgress(100);
        progress.WriteLine($">>> 协议执行完毕 | 耗时 {stopwatch.Elapsed.TotalSeconds:F1} 秒 <<<");
    }

    /// <summary>准备：校验登录 Cookie、补齐设备指纹、确认登录态。</summary>
    private async Task<NavData> PrepareAsync(IJobProgress progress, CancellationToken cancellationToken)
    {
        if (!account.Cookie.IsAuthenticated) { throw new InvalidOperationException("未配置有效 Cookie（需要 SESSDATA 与 bili_jct）。请扫码登录 POST /api/bilibili/login/qrcode，或设置 Bilibili:Cookie / data/bilibili-cookie.txt。"); }

        await account.EnsureBrowserCookiesAsync(cancellationToken);
        progress.WriteLine(account.Cookie.HasDeviceId ? "[认证] 设备指纹已注入（buvid）" : "[认证] 警告：设备指纹缺失 buvid3，部分链路可能触发风控");
        progress.SetProgress(5);

        var nav = await account.GetNavAsync(cancellationToken);
        EnsureSuccess(nav, "登录校验");
        if (nav.Data is not { IsLogin: true }) { throw new InvalidOperationException("Cookie 无效或已过期，导航接口显示未登录。"); }

        progress.WriteLine($"[身份] {BilibiliNameMask.Mask(nav.Data.Uname)} | UID={nav.Data.Mid} | 硬币={nav.Data.Money}");
        progress.SetProgress(10);
        return nav.Data;
    }

    /// <summary>扫描每日任务完成状态；失败时按未完成继续。</summary>
    private async Task<DailyTaskInfo> ScanDailyStatusAsync(IJobProgress progress, CancellationToken cancellationToken)
    {
        var daily = await account.GetDailyTaskAsync(cancellationToken);
        if (!daily.IsSuccess)
        {
            progress.WriteLine($"[扫描] 任务扫描异常 code={daily.Code} {daily.DisplayMessage}（继续执行）");
            logger.LogWarning("读取每日任务状态失败：code={Code} {Message}", daily.Code, daily.DisplayMessage);
            progress.SetProgress(20);
            return new DailyTaskInfo();
        }

        var info = daily.Data ?? new DailyTaskInfo();
        progress.WriteLine($"[扫描] 登录[{Status(info.Login)}] 观看[{Status(info.Watch)}] 分享[{Status(info.Share)}] 投币经验={info.Coins}");
        progress.SetProgress(20);
        return info;
    }

    private static void EnsureSuccess<T>(BilibiliApiResponse<T> response, string step)
    {
        if (!response.IsSuccess)
        {
            throw new InvalidOperationException($"{step}失败：code={response.Code} {response.DisplayMessage}");
        }
    }

    private static string Status(bool ok) => ok ? "已完成" : "待执行";
}
