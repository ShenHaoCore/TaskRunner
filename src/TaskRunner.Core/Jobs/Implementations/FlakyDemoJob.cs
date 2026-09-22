using TaskRunner.Core.Jobs.Attributes;

namespace TaskRunner.Core.Jobs.Implementations;

[RecurringTask("0 0 0 1 1 *", Description = "手动触发以观察失败重试，默认禁用", Enabled = false)]
public sealed class FlakyDemoJob : IRecurringJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException("演示失败：Hangfire 会按指数退避重试，最多 3 次。");
    }
}
