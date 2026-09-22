using Microsoft.Extensions.Logging;
using TaskRunner.Core.Jobs.Attributes;
using TaskRunner.Core.Services;

namespace TaskRunner.Core.Jobs.Implementations;

[RecurringTask("*/5 * * * * *", Description = "每5秒同步一次数据")]
[ExclusiveExecution(60)]
public sealed class SyncDataJob(ILogger<SyncDataJob> logger, TimeProvider time, ISyncWindowStore store) : IRecurringJob
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var window = time.GetUtcNow().ToString("yyyyMMddHHmmss");
        if (!store.TryBegin(window))
        {
            logger.LogInformation("同步窗口 {Window} 已处理，跳过重复执行。", window);
            return;
        }

        try
        {
            logger.LogInformation("同步窗口 {Window} 完成。", window);
            await Task.CompletedTask;
        }
        catch
        {
            store.Forget(window);
            throw;
        }
    }
}
