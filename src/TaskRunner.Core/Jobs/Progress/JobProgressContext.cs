namespace TaskRunner.Core.Jobs.Progress;

/// <summary>
/// 同一 Hangfire 作业 Scope 内共享的进度实现。由 Hangfire 层在执行前写入。
/// </summary>
public sealed class JobProgressContext
{
    public IJobProgress Current { get; set; } = NullJobProgress.Instance;
}
