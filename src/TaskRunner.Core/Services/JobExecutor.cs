using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaskRunner.Core.Common;
using TaskRunner.Core.Jobs;

namespace TaskRunner.Core.Services;

public sealed class JobExecutor(IServiceProvider services, ILogger<JobExecutor> logger)
{
    public Task ExecuteRecurringAsync(string jobId, string jobTypeName, CancellationToken cancellationToken)
        => ExecuteAsync(jobId, jobTypeName, typeof(IRecurringJob), cancellationToken);

    public Task ExecuteBackgroundAsync(string jobId, string jobTypeName, CancellationToken cancellationToken)
        => ExecuteAsync(jobId, jobTypeName, typeof(IBackgroundJob), cancellationToken);

    private async Task ExecuteAsync(string jobId, string jobTypeName, Type serviceType, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var type = JobTypeResolver.Resolve(jobTypeName);
            if (!serviceType.IsAssignableFrom(type))
            {
                throw new InvalidOperationException($"类型 {jobTypeName} 没有实现 {serviceType.Name}。");
            }

            logger.LogInformation("任务 {JobId} 开始执行，类型 {JobType}。", jobId, jobTypeName);
            var service = services.GetRequiredService(type);
            switch (service)
            {
                case IRecurringJob recurring when serviceType == typeof(IRecurringJob):
                    await recurring.ExecuteAsync(cancellationToken);
                    break;
                case IBackgroundJob background when serviceType == typeof(IBackgroundJob):
                    await background.ExecuteAsync(cancellationToken);
                    break;
                default:
                    throw new InvalidOperationException($"无法执行任务类型 {jobTypeName}。");
            }

            logger.LogInformation("任务 {JobId} 执行成功。", jobId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("任务 {JobId} 已取消。", jobId);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "任务 {JobId} 执行失败。", jobId);
            throw;
        }
    }
}
