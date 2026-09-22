using Hangfire;
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
        recurring.AddOrUpdate<JobExecutor>(
            jobId,
            executor => executor.ExecuteRecurringAsync(jobId, jobType, CancellationToken.None),
            cron,
            Options);
    }

    public void RemoveRecurring(string jobId) => recurring.RemoveIfExists(jobId);

    public string EnqueueRecurring(string jobId, string jobType)
        => jobs.Enqueue<JobExecutor>(executor => executor.ExecuteRecurringAsync(jobId, jobType, CancellationToken.None));

    public string EnqueueBackground(string jobId, string jobType)
        => jobs.Enqueue<JobExecutor>(executor => executor.ExecuteBackgroundAsync(jobId, jobType, CancellationToken.None));
}
