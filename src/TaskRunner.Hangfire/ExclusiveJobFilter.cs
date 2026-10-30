using Hangfire.Common;
using Hangfire.Server;
using Hangfire.Storage;
using Microsoft.Extensions.Logging;
using TaskRunner.Core.Common;
using TaskRunner.Core.Jobs.Attributes;

namespace TaskRunner.Hangfire;

/// <summary>
/// 按任务 JobId 互斥；仅对标记了 <see cref="ExclusiveExecutionAttribute"/> 的任务生效。
/// </summary>
public sealed class ExclusiveJobFilter(ILogger<ExclusiveJobFilter>? logger = null) : IServerFilter
{
    private const string LockItemKey = "TaskRunner.ExclusiveLock";

    public void OnPerforming(PerformingContext context)
    {
        var job = context.BackgroundJob.Job;
        if (job?.Args is null || job.Args.Count < 2)
        {
            return;
        }

        if (!TryResolveLockKey(job.Args, out var lockKey, out var timeout))
        {
            return;
        }

        var distributedLock = context.Connection.AcquireDistributedLock(lockKey, timeout);
        context.Items[LockItemKey] = distributedLock;
    }

    public void OnPerformed(PerformedContext context)
    {
        if (context.Items.TryGetValue(LockItemKey, out var value) && value is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    /// <summary>
    /// 从作业参数解析互斥锁键：<c>exclusive-job:{jobId}</c> 与超时。返回 false 表示不互斥
    /// （参数形态不符 / 类型解析失败 / 未标记 ExclusiveExecutionAttribute）；
    /// 类型解析失败时记 Warning——互斥静默失效会让并发执行真实发生，必须可观测。
    /// </summary>
    internal bool TryResolveLockKey(IReadOnlyList<object?> args, out string lockKey, out TimeSpan timeout)
    {
        lockKey = string.Empty;
        timeout = TimeSpan.Zero;

        if (args.Count < 2
            || args[0] is not string jobId || string.IsNullOrWhiteSpace(jobId)
            || args[1] is not string jobTypeName || string.IsNullOrWhiteSpace(jobTypeName))
        {
            return false;
        }

        Type type;
        try
        {
            type = JobTypeResolver.Resolve(jobTypeName);
        }
        catch (InvalidOperationException ex)
        {
            logger?.LogWarning(
                "任务 {JobId} 类型解析失败（{JobType}），本次执行不互斥：{Message}。",
                jobId, jobTypeName, ex.Message);
            return false;
        }

        var exclusive = type.GetCustomAttributes(typeof(ExclusiveExecutionAttribute), inherit: false)
            .OfType<ExclusiveExecutionAttribute>()
            .FirstOrDefault();
        if (exclusive is null)
        {
            return false;
        }

        lockKey = $"exclusive-job:{jobId}";
        timeout = TimeSpan.FromSeconds(Math.Max(1, exclusive.TimeoutSeconds));
        return true;
    }
}
