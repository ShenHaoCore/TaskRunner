using Hangfire;
using Hangfire.Storage;
using TaskRunner.Core.Common;
using TaskRunner.Core.Services;

namespace TaskRunner.Hangfire;

public sealed class HangfireTaskScheduler(IBackgroundJobClient jobs, IRecurringJobManager recurring) : ITaskScheduler
{
    private static readonly RecurringJobOptions Options = new()
    {
        TimeZone = TimeZoneInfo.Utc
    };

    public void AddOrUpdateRecurring(string jobId, string jobType, string cron)
    {
        CronExpressionGuard.EnsureValid(cron);
        // PerformContext / IJobCancellationToken 入队时传 null，由 Hangfire 运行时注入。
        recurring.AddOrUpdate<HangfireJobGateway>(
            jobId,
            gateway => gateway.ExecuteAsync(jobId, jobType, null!, null!),
            cron,
            Options);
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

    public string Enqueue(string jobId, string jobType)
        => jobs.Enqueue<HangfireJobGateway>(gateway =>
            gateway.ExecuteAsync(jobId, jobType, null!, null!));
}
