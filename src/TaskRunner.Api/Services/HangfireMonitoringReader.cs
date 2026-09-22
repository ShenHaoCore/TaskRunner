using Hangfire;
using Hangfire.Common;
using Hangfire.Storage;
using Hangfire.Storage.Monitoring;
using TaskRunner.Core.Models;

namespace TaskRunner.Api.Services;

public sealed class HangfireMonitoringReader(JobStorage storage, ILogger<HangfireMonitoringReader> logger)
{
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
