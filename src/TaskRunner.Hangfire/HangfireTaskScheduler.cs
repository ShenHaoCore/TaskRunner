using Hangfire;
using Hangfire.Storage;
using TaskRunner.Core.Common;
using TaskRunner.Core.Services;

namespace TaskRunner.Hangfire;

public sealed class HangfireTaskScheduler(IBackgroundJobClient jobs, IRecurringJobManager recurring) : ITaskScheduler
{
    public void AddOrUpdateRecurring(string jobId, string jobType, string cron, string queue)
    {
        CronExpressionGuard.EnsureValid(cron);
        var queueName = QueueNameGuard.Normalize(queue);
        // PerformContext / IJobCancellationToken 入队时传 null，由 Hangfire 运行时注入。
        recurring.AddOrUpdate<HangfireJobGateway>(
            jobId,
            queueName,
            gateway => gateway.ExecuteAsync(jobId, jobType, null!, null!),
            cron,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
    }

    public void RemoveRecurring(string jobId) => recurring.RemoveIfExists(jobId);

    public IReadOnlyList<string> PruneRecurringExcept(IReadOnlyCollection<string> keepJobIds)
    {
        var keep = keepJobIds as HashSet<string> ?? keepJobIds.ToHashSet(StringComparer.Ordinal);
        var removed = new List<string>();
        using var connection = JobStorage.Current.GetConnection();
        foreach (var job in connection.GetRecurringJobs())
        {
            if (keep.Contains(job.Id))
            {
                continue;
            }

            recurring.RemoveIfExists(job.Id);
            removed.Add(job.Id);
        }

        return removed;
    }

    public string Enqueue(string jobId, string jobType, string queue)
    {
        var queueName = QueueNameGuard.Normalize(queue);
        return jobs.Enqueue(queueName, (HangfireJobGateway gateway) =>
            gateway.ExecuteAsync(jobId, jobType, null!, null!));
    }

    public string Schedule(string jobId, string jobType, string queue, DateTimeOffset runAt)
    {
        var queueName = QueueNameGuard.Normalize(queue);
        return jobs.Schedule(queueName, (HangfireJobGateway gateway) =>
            gateway.ExecuteAsync(jobId, jobType, null!, null!), runAt);
    }
}
