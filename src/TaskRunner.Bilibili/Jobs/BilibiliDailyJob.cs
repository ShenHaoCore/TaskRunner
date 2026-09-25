using TaskRunner.Core.Jobs;
using TaskRunner.Core.Jobs.Attributes;
using TaskRunner.Core.Jobs.Progress;

namespace TaskRunner.Bilibili.Jobs;

/// <summary>
/// 
/// </summary>
/// <param name="pipeline"></param>
/// <param name="progress"></param>
[RecurringTask("0 0 9 * * *", Name = "B站每日任务", Description = "登录校验、观看/分享/投币、漫画签到、直播签到、银瓜子兑硬币（需配置 Bilibili:Cookie）")]
[ExclusiveExecution(3600)]
public sealed class BilibiliDailyJob(IBilibiliDailyPipeline pipeline, JobProgressContext progress) : IRecurringJob
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task ExecuteAsync(CancellationToken cancellationToken) => pipeline.RunAsync(progress.Current, cancellationToken);
}