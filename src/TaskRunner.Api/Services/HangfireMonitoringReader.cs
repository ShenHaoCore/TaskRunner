using Hangfire;
using Hangfire.Common;
using Hangfire.Storage;
using Hangfire.Storage.Monitoring;
using TaskRunner.Core.Jobs;
using TaskRunner.Core.Models;
using TaskRunner.Core.Services;

namespace TaskRunner.Api.Services;

public sealed class HangfireMonitoringReader(JobStorage storage, IJobCatalog catalog, ILogger<HangfireMonitoringReader> logger)
{
    /// <summary>读取平台心跳的最近/下次执行；recurring 未注册（如开关关闭）时返回 null。</summary>
    public (DateTime? LastExecution, DateTime? NextExecution)? ReadHeartbeat()
    {
        var recurring = ReadRecurringJobs();
        return recurring.TryGetValue(HeartbeatJob.JobIdValue, out var job)
            ? (job.LastExecution, job.NextExecution)
            : null;
    }

    public JobStatistics ReadStatistics()
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

    /// <summary>
    /// 近 N 小时的成功/失败按小时计数（Hangfire stats 自带按小时聚合，无自建存储）。
    /// 聚合桶的时区由存储实现决定，故不按绝对时刻过滤，按桶序取最近 N 个并升序返回。
    /// 读取失败时优雅降级为空列表（监控读侧不拖垮页面）。
    /// </summary>
    public IReadOnlyList<TrendPoint> ReadHourlyTrend(int hours = 24)
    {
        hours = Math.Clamp(hours, 1, 24);
        try
        {
            var monitoring = storage.GetMonitoringApi();
            var succeeded = monitoring.HourlySucceededJobs();
            var failed = monitoring.HourlyFailedJobs();
            return succeeded.Keys.Union(failed.Keys)
                .OrderByDescending(hour => hour)
                .Take(hours)
                .OrderBy(hour => hour)
                .Select(hour => new TrendPoint(
                    hour,
                    succeeded.TryGetValue(hour, out var ok) ? ok : 0,
                    failed.TryGetValue(hour, out var bad) ? bad : 0))
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "读取小时级执行趋势失败。");
            return [];
        }
    }

    /// <summary>
    /// 全站最近执行（成功 + 失败混合，按时间倒序）。jobId 取自 Gateway 作业参数首位的约定，
    /// 显示名从任务目录补齐；读取失败时优雅降级为空列表。
    /// </summary>
    public IReadOnlyList<RecentExecution> ReadRecentExecutions(int limit = 8)
    {
        limit = Math.Clamp(limit, 1, 50);
        var items = new List<RecentExecution>();
        var monitoring = storage.GetMonitoringApi();
        var displayNames = catalog.RecurringJobs
            .GroupBy(job => job.JobId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().JobName, StringComparer.Ordinal);

        try
        {
            foreach (var pair in monitoring.SucceededJobs(0, limit * 3))
            {
                var dto = pair.Value;
                var jobId = ExtractJobId(dto?.Job);
                if (jobId is null)
                {
                    continue;
                }

                items.Add(new RecentExecution(
                    jobId,
                    displayNames.GetValueOrDefault(jobId),
                    "Succeeded",
                    dto?.SucceededAt,
                    null,
                    dto?.TotalDuration));
                if (items.Count >= limit * 2)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "读取最近成功作业失败。");
        }

        try
        {
            foreach (var pair in monitoring.FailedJobs(0, limit * 3))
            {
                var dto = pair.Value;
                var jobId = ExtractJobId(dto?.Job);
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

    /// <summary>Gateway 入队约定：Args[0] = jobId。非本平台作业（无该约定）返回 null 不展示。</summary>
    private static string? ExtractJobId(Job? job)
        => job?.Args is { Count: > 0 } args && args[0] is string jobId && !string.IsNullOrWhiteSpace(jobId)
            ? jobId
            : null;

    /// <summary>读取单任务视图（列表逻辑的便捷封装）。</summary>
    public TaskConfigView ReadOne(TaskConfig config) => ReadTasks([config])[0];

    public IReadOnlyList<TaskConfigView> ReadTasks(IReadOnlyList<TaskConfig> configs)
    {
        var recurring = ReadRecurringJobs();
        return configs.Select(config =>
        {
            recurring.TryGetValue(config.JobId, out var job);
            return new TaskConfigView(
                config.JobId,
                config.JobName,
                config.CronExpr,
                config.IsEnabled,
                config.JobType,
                config.Description,
                config.EffectiveQueue,
                job?.LastExecution,
                job?.NextExecution,
                job?.LastJobState,
                job?.Error);
        }).ToList();
    }

    public IReadOnlyList<JobHistoryItem> ReadHistory(string jobId, int limit)
    {
        limit = Math.Clamp(limit, 1, 100);
        var items = new List<JobHistoryItem>();
        var monitoring = storage.GetMonitoringApi();

        AppendLastJobHistory(monitoring, jobId, items);
        if (items.Count >= limit)
        {
            return items
                .OrderByDescending(item => item.OccurredAt ?? DateTime.MinValue)
                .Take(limit)
                .ToList();
        }

        var take = Math.Min(limit * 5, 200);
        try
        {
            foreach (var pair in monitoring.SucceededJobs(0, take))
            {
                if (!Matches(pair.Value?.Job, jobId))
                {
                    continue;
                }

                items.Add(new JobHistoryItem(pair.Key, "Succeeded", pair.Value?.SucceededAt, null));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "读取成功任务历史失败。");
        }

        try
        {
            foreach (var pair in monitoring.FailedJobs(0, take))
            {
                if (!Matches(pair.Value?.Job, jobId))
                {
                    continue;
                }

                var reason = pair.Value?.ExceptionMessage ?? pair.Value?.Reason;
                items.Add(new JobHistoryItem(pair.Key, "Failed", pair.Value?.FailedAt, reason));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "读取失败任务历史失败。");
        }

        return items
            .GroupBy(item => item.JobId + "|" + item.State + "|" + item.OccurredAt, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderByDescending(item => item.OccurredAt ?? DateTime.MinValue)
            .Take(limit)
            .ToList();
    }

    private void AppendLastJobHistory(IMonitoringApi monitoring, string jobId, List<JobHistoryItem> items)
    {
        try
        {
            var recurring = ReadRecurringJobs();
            if (!recurring.TryGetValue(jobId, out var job) || string.IsNullOrWhiteSpace(job.LastJobId))
            {
                return;
            }

            var details = monitoring.JobDetails(job.LastJobId);
            if (details?.History is null)
            {
                return;
            }

            foreach (var state in details.History)
            {
                items.Add(new JobHistoryItem(job.LastJobId, state.StateName, state.CreatedAt, state.Reason));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "读取任务 {JobId} 的最近一次状态失败。", jobId);
        }
    }

    private Dictionary<string, RecurringJobDto> ReadRecurringJobs()
    {
        try
        {
            using var connection = storage.GetConnection();
            return connection.GetRecurringJobs()
                .Where(job => !string.IsNullOrWhiteSpace(job.Id))
                .GroupBy(job => job.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "读取 Hangfire 定时任务失败。");
            return new Dictionary<string, RecurringJobDto>(StringComparer.Ordinal);
        }
    }

    private static bool Matches(Job? job, string jobId)
    {
        if (job?.Args is null)
        {
            return false;
        }

        foreach (var arg in job.Args)
        {
            if (arg is string text && string.Equals(text, jobId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
