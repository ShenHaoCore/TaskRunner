using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskRunner.Bilibili.Api;
using TaskRunner.Bilibili.Models;
using TaskRunner.Core.Jobs.Progress;

namespace TaskRunner.Bilibili.Tasks;

/// <summary>每日投币任务：按目标数量投币，支持 34005 换源重试。</summary>
public sealed class CoinTask(
    IBilibiliVideoApi video,
    IBilibiliVideoPicker picker,
    IOptions<BilibiliOptions> options,
    ILogger<CoinTask> logger) : BilibiliTaskBase(logger), ICoinTask
{
    public async Task ExecuteAsync(IJobProgress progress, NavData nav, DailyTaskInfo info, CancellationToken cancellationToken)
    {
        var quota = Math.Clamp(options.Value.NumberOfCoins, 0, 5);
        if (quota <= 0)
        {
            progress.WriteLine("[投币任务] 跳过（目标=0）");
            return;
        }

        var already = (int)Math.Min(info.Coins / 10, 5);
        var need = Math.Max(0, quota - already);
        progress.WriteLine($"[投币任务] 目标={quota} | 已获={already} | 还需={need}");

        var success = 0;
        var attempts = 0;
        // 随机到已投过的视频时（34005）换一个继续，不占用投币名额；余额不足（-104）等硬错误才终止。
        while (success < need && attempts < need + 3)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempts++;
            var index = success + 1;
            var target = await picker.PickAsync(nav.Mid, cancellationToken);
            progress.WriteLine($"[目标-投币 {index}/{need}] {target.Bvid}《{target.Title}》(aid={target.Aid} | 来源={target.Source})");
            var coin = await GuardAsync($"投币 {index}/{need}",
                () => video.AddCoinAsync(target.Aid, target.Bvid, multiply: 1, cancellationToken), cancellationToken);
            if (coin.IsSuccess)
            {
                success++;
                progress.WriteLine($"[投币 {index}/{need}] 同步完成");
            }
            else if (coin.Code is 34005)
            {
                progress.WriteLine($"[投币 {index}/{need}] 该视频已投过，换一个");
            }
            else
            {
                LogStep(progress, $"投币 {index}/{need}", coin);
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        if (success < need)
        {
            progress.WriteLine($"[投币任务] 完成 {success}/{need}（尝试 {attempts} 次）");
        }
    }
}
