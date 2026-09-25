using Hangfire.States;
using Hangfire.Storage;

namespace TaskRunner.Hangfire;

/// <summary>
/// 缩短 Succeeded / Deleted 作业在 Hangfire 存储中的保留时间，避免高频任务（如 sync-data）撑爆历史。
/// </summary>
public sealed class JobExpirationFilter(TimeSpan succeededExpiration) : IApplyStateFilter
{
    public void OnStateApplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
        if (context.NewState is SucceededState or DeletedState)
        {
            context.JobExpirationTimeout = succeededExpiration;
        }
    }

    public void OnStateUnapplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
    }
}
