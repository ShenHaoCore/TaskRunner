using Microsoft.Extensions.Logging;
using TaskRunner.Core.Jobs;
using TaskRunner.Core.Jobs.Attributes;
using TaskRunner.Core.Jobs.Progress;

namespace TaskRunner.PlatformJobs;

/// <summary>
/// 每日运行日报：把平台累计统计与最近失败写到作业详情页（Console）与文件日志各一份，
/// 给运维一条可检索的日报记录。数据经 <see cref="IJobInsights"/>（引擎侧实现，作业零 Hangfire 依赖）。
/// </summary>
[RecurringTask("0 0 8 * * *", JobId = DailyDigestJob.JobIdValue, Name = "每日运行日报",
    Description = "每天 08:00 输出运行日报：累计统计快照 + 最近失败 top5（作业详情页与日志各一份）。")]
public sealed class DailyDigestJob(
    IJobInsights insights,
    IJobProgress progress,
    ILogger<DailyDigestJob> logger) : IRecurringJob
{
    public const string JobIdValue = "daily-digest";

    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var stats = insights.GetStatistics();
        progress.WriteLine("── 运行日报 ──");
        progress.WriteLine(
            $"累计：成功 {stats.Succeeded} · 失败 {stats.Failed} · 排队 {stats.Enqueued} · 处理中 {stats.Processing} · " +
            $"已预约 {stats.Scheduled} · 定时任务 {stats.Recurring} · Worker {stats.Servers}");
        logger.LogInformation(
            "运行日报：成功 {Succeeded}，失败 {Failed}，排队 {Enqueued}，处理中 {Processing}，已预约 {Scheduled}，定时任务 {Recurring}，Worker {Servers}。",
            stats.Succeeded, stats.Failed, stats.Enqueued, stats.Processing, stats.Scheduled, stats.Recurring, stats.Servers);
        progress.SetProgress(40);

        cancellationToken.ThrowIfCancellationRequested();

        var failures = insights.RecentFailures(5);
        if (failures.Count == 0)
        {
            progress.WriteLine("最近失败：无 ✓");
        }
        else
        {
            progress.WriteLine($"最近失败 {failures.Count} 条（详见 Dashboard）：");
            foreach (var failure in failures)
            {
                progress.WriteLine($"  ✕ {failure.JobName ?? failure.JobId}（{failure.JobId}）{Truncate(failure.Reason, 120)}");
            }
        }

        progress.SetProgress(100);
        progress.WriteLine("── 日报结束 ──");
        return Task.CompletedTask;
    }

    private static string Truncate(string? text, int max)
        => string.IsNullOrEmpty(text) ? string.Empty
            : text.Length <= max ? text
            : text[..max] + "…";
}
