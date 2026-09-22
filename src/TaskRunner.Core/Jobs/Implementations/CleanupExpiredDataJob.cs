using Microsoft.Extensions.Logging;
using TaskRunner.Core.Jobs.Attributes;
using TaskRunner.Core.Services;

namespace TaskRunner.Core.Jobs.Implementations;

[RecurringTask("0 * * * * *", Description = "每分钟清理过期的同步窗口")]
public sealed class CleanupExpiredDataJob(ILogger<CleanupExpiredDataJob> logger, TimeProvider time, ISyncWindowStore store) : IRecurringJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var removed = store.RemoveOlderThan(time.GetUtcNow().AddHours(-1));
        logger.LogInformation("清理过期同步窗口 {Count} 条。", removed);
        return Task.CompletedTask;
    }
}
