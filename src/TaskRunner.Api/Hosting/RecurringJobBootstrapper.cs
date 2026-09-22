using TaskRunner.Core.Services;

namespace TaskRunner.Api.Hosting;

public sealed class RecurringJobBootstrapper(
    IServiceScopeFactory scopeFactory,
    IJobCatalog catalog,
    ITaskScheduler scheduler,
    ILogger<RecurringJobBootstrapper> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITaskConfigRepository>();
        await repository.SyncDiscoveredAsync(catalog.RecurringJobs, cancellationToken);

        var discoveredIds = catalog.RecurringJobs.Select(job => job.JobId).ToList();
        var removed = await repository.RemoveMissingAsync(discoveredIds, cancellationToken);
        foreach (var jobId in removed)
        {
            scheduler.RemoveRecurring(jobId);
            logger.LogInformation("已清理废弃定时任务 {JobId}（配置与 Hangfire recurring）。", jobId);
        }

        var configs = await repository.ListAsync(cancellationToken);
        var discovered = discoveredIds.ToHashSet(StringComparer.Ordinal);

        foreach (var config in configs)
        {
            if (!discovered.Contains(config.JobId))
            {
                continue;
            }

            try
            {
                if (!config.IsEnabled)
                {
                    scheduler.RemoveRecurring(config.JobId);
                    logger.LogInformation("定时任务 {JobId} 已暂停，未注册到 Hangfire。", config.JobId);
                    continue;
                }

                scheduler.AddOrUpdateRecurring(config.JobId, config.JobType, config.CronExpr);
                logger.LogInformation("定时任务 {JobId} 已注册，Cron={Cron}。", config.JobId, config.CronExpr);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "定时任务 {JobId} 注册失败。", config.JobId);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
