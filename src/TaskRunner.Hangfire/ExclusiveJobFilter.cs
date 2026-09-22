using Hangfire.Common;
using Hangfire.Server;
using Hangfire.Storage;
using TaskRunner.Core.Common;
using TaskRunner.Core.Jobs.Attributes;

namespace TaskRunner.Hangfire;

/// <summary>
/// 按任务 JobId 互斥；仅对标记了 <see cref="ExclusiveExecutionAttribute"/> 的任务生效。
/// </summary>
public sealed class ExclusiveJobFilter : IServerFilter
{
    private const string LockItemKey = "TaskRunner.ExclusiveLock";

    public void OnPerforming(PerformingContext filterContext)
    {
        var job = filterContext.BackgroundJob.Job;
        if (job?.Args is null || job.Args.Count < 2)
        {
            return;
        }

        if (job.Args[0] is not string jobId || string.IsNullOrWhiteSpace(jobId))
        {
            return;
        }

        if (job.Args[1] is not string jobTypeName || string.IsNullOrWhiteSpace(jobTypeName))
        {
            return;
        }

        Type type;
        try
        {
            type = JobTypeResolver.Resolve(jobTypeName);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        var exclusive = type.GetCustomAttributes(typeof(ExclusiveExecutionAttribute), inherit: false)
            .OfType<ExclusiveExecutionAttribute>()
            .FirstOrDefault();
        if (exclusive is null)
        {
            return;
        }

        var timeout = TimeSpan.FromSeconds(Math.Max(1, exclusive.TimeoutSeconds));
        var distributedLock = filterContext.Connection.AcquireDistributedLock($"exclusive-job:{jobId}", timeout);
        filterContext.Items[LockItemKey] = distributedLock;
    }

    public void OnPerformed(PerformedContext filterContext)
    {
        if (filterContext.Items.TryGetValue(LockItemKey, out var value) && value is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
