using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskRunner.Bilibili.Api;
using TaskRunner.Bilibili.Models;
using TaskRunner.Core.Jobs.Progress;

namespace TaskRunner.Bilibili.Tasks;

/// <summary>每日点赞任务：选视频并点赞（独立于投币的 select_like）。</summary>
public sealed class LikeTask(
    IBilibiliVideoApi video,
    IBilibiliVideoPicker picker,
    IOptions<BilibiliOptions> options,
    ILogger<LikeTask> logger) : BilibiliTaskBase(logger), ILikeTask
{
    public async Task ExecuteAsync(IJobProgress progress, NavData nav, DailyTaskInfo info, CancellationToken cancellationToken)
    {
        if (!options.Value.EnableLike)
        {
            progress.WriteLine("[点赞] 离线（配置已关闭）");
            return;
        }

        var target = await picker.PickAsync(nav.Mid, cancellationToken);
        progress.WriteLine($"[目标-点赞] {target.Bvid}《{target.Title}》(aid={target.Aid} | 来源={target.Source})");

        var like = await GuardAsync("点赞",
            () => video.LikeAsync(target.Aid, target.Bvid, cancellationToken), cancellationToken);
        LogStep(progress, "点赞", like);
    }
}
