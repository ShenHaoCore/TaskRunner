using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaskRunner.Core.Common;
using TaskRunner.Core.Jobs;

namespace TaskRunner.Core.Services;

public sealed class JobExecutor(IServiceProvider services, ILogger<JobExecutor> logger)
{
    public async Task ExecuteAsync(string jobId, string jobTypeName, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!JobTypeResolver.TryResolve(jobTypeName, out var type) || type is null)
            {
                // 代码已删除但队列仍可能入队：直接出队，避免重试刷屏。
                logger.LogWarning("任务 {JobId} 类型已不存在，跳过：{JobType}。", jobId, jobTypeName);
                return;
            }

            if (!typeof(IRecurringJob).IsAssignableFrom(type))
            {
                throw new InvalidOperationException($"类型 {jobTypeName} 没有实现 IRecurringJob。");
            }

            logger.LogInformation("任务 {JobId} 开始执行，类型 {JobType}。", jobId, jobTypeName);
            var job = (IRecurringJob)services.GetRequiredService(type);
            await job.ExecuteAsync(cancellationToken);
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
