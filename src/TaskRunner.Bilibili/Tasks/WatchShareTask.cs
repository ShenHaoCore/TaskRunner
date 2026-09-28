using Microsoft.Extensions.Logging;
using TaskRunner.Bilibili.Api;
using TaskRunner.Bilibili.Models;
using TaskRunner.Core.Jobs.Progress;

namespace TaskRunner.Bilibili.Tasks;

/// <summary>观看与分享任务：选视频 → 补 cid → 心跳上报 → 分享。</summary>
public sealed class WatchShareTask(
    IBilibiliVideoApi video,
    IBilibiliVideoPicker picker,
    ILogger<WatchShareTask> logger) : BilibiliTaskBase(logger), IWatchShareTask
{
    public async Task ExecuteAsync(IJobProgress progress, NavData nav, DailyTaskInfo info, CancellationToken cancellationToken)
    {
        var target = await picker.PickAsync(nav.Mid, cancellationToken);
        progress.WriteLine($"[目标-观看分享] {target.Bvid}《{target.Title}》(aid={target.Aid} | 来源={target.Source})");

        var cid = target.Cid;
        // 观看或分享任一待执行时都需要 cid（分享前要补一次打开视频的心跳）。
        if ((!info.Watch || !info.Share) && cid <= 0)
        {
            cid = await ResolveCidAsync(target, cancellationToken);
        }

        var videoOpened = false;
        if (!info.Watch)
        {
            var hb = await GuardAsync("观看上报",
                () => video.HeartbeatAsync(target.Aid, cid, target.Bvid, nav.Mid, playedTime: 15, cancellationToken), cancellationToken);
            LogStep(progress, "观看上报", hb);
            videoOpened = hb.IsSuccess;
        }
        else
        {
            progress.WriteLine("[观看上报] 跳过（今日已同步）");
        }

        if (!info.Share)
        {
            // 本次没有播放上下文时，先上报一次打开视频（played_time=0），模拟真实分享路径。
            if (!videoOpened && cid > 0)
            {
                await GuardAsync("打开视频",
                    () => video.HeartbeatAsync(target.Aid, cid, target.Bvid, nav.Mid, playedTime: 0, cancellationToken), cancellationToken);
            }

            var share = await GuardAsync("分享视频",
                () => video.ShareVideoAsync(target.Aid, target.Bvid, cancellationToken), cancellationToken);
            LogStep(progress, "分享视频", share);
        }
        else
        {
            progress.WriteLine("[分享视频] 跳过（今日已同步）");
        }
    }

    private async Task<long> ResolveCidAsync(VideoTarget target, CancellationToken cancellationToken)
    {
        try
        {
            var view = await video.GetVideoViewAsync(target.Bvid, target.Aid, cancellationToken);
            return view.IsSuccess ? view.Data!.Cid : 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation(ex, "补取稿件 cid 失败（aid={Aid}）。", target.Aid);
            return 0;
        }
    }
}
