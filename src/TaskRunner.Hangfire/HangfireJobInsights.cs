using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.Logging;
using TaskRunner.Core.Jobs;
using TaskRunner.Core.Models;
using TaskRunner.Core.Services;

namespace TaskRunner.Hangfire;

/// <summary>
/// <see cref="IJobInsights"/> 的 Hangfire 实现：统计快照 + 最近失败扫描。
/// 失败作业不受成功保留期影响，数据可靠；读侧异常降级为空列表，不拖垮调用它的作业。
/// 与 Api 侧 HangfireMonitoringReader 的同构逻辑为有意重复（页面读侧与作业读侧独立演进）。
/// </summary>
public sealed class HangfireJobInsights(JobStorage storage, IJobCatalog catalog, ILogger<HangfireJobInsights> logger) : IJobInsights
{
    public JobStatistics GetStatistics()
    {
        var stats = storage.GetMonitoringApi().GetStatistics();
        return new JobStatistics(
            stats.Succeeded,
            stats.Failed,
            stats.Enqueued,
            stats.Processing,
            stats.Scheduled,
            stats.Recurring,
            stats.Servers);
    }

    public IReadOnlyList<RecentExecution> RecentFailures(int limit)
    {
        limit = Math.Clamp(limit, 1, 50);
        var items = new List<RecentExecution>();
        var displayNames = catalog.RecurringJobs
            .GroupBy(job => job.JobId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().JobName, StringComparer.Ordinal);

        try
        {
            var monitoring = storage.GetMonitoringApi();
            foreach (var pair in monitoring.FailedJobs(0, limit * 3))
            {
                var dto = pair.Value;
                var jobId = dto?.Job?.Args is { Count: > 0 } args
                            && args[0] is string id && !string.IsNullOrWhiteSpace(id)
                    ? id
                    : null;
                if (jobId is null)
                {
                    continue;
                }

                items.Add(new RecentExecution(
                    jobId,
                    displayNames.GetValueOrDefault(jobId),
                    "Failed",
                    dto?.FailedAt,
                    dto?.ExceptionMessage ?? dto?.Reason,
                    null));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "读取最近失败作业失败。");
        }

        return items
            .OrderByDescending(item => item.OccurredAt ?? DateTime.MinValue)
            .Take(limit)
            .ToList();
    }
}
