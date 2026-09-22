using Microsoft.Extensions.Logging;
using TaskRunner.Core.Jobs.Attributes;

namespace TaskRunner.Core.Jobs.Implementations;

[BackgroundTask(Description = "发送通知。重复执行不会产生额外副作用。")]
public sealed class SendNotificationJob(ILogger<SendNotificationJob> logger) : IBackgroundJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation("通知已发送。重复执行不会产生额外副作用。");
        return Task.CompletedTask;
    }
}
