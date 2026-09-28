using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskRunner.Bilibili.Api;
using TaskRunner.Core.Jobs.Progress;

namespace TaskRunner.Bilibili.Tasks;

/// <summary>直播签到任务：识别官方下线（code=1）为跳过而非失败。</summary>
public sealed class LiveSignTask(
    IBilibiliLiveApi live,
    IOptions<BilibiliOptions> options,
    ILogger<LiveSignTask> logger) : BilibiliTaskBase(logger), ILiveSignTask
{
    public async Task ExecuteAsync(IJobProgress progress, CancellationToken cancellationToken)
    {
        if (!options.Value.EnableLiveSign)
        {
            progress.WriteLine("[直播节点] 离线（配置已关闭）");
            return;
        }

        var result = await GuardAsync("直播节点", () => live.SignAsync(cancellationToken), cancellationToken);
        // code=1「签到活动已下线」是 B 站长期下线该活动，不是失败。
        if (!result.IsSuccess && result.Code is 1)
        {
            progress.WriteLine($"[直播节点] 跳过（{result.DisplayMessage}）");
        }
        else
        {
            LogStep(progress, "直播节点", result);
        }
    }
}
