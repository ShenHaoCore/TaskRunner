using TaskRunner.Core.Services;

namespace TaskRunner.Api.Hosting;

/// <summary>
/// 在 Hangfire Server StartAsync 之前同步 recurring（IHostedLifecycleService.StartingAsync）。
/// </summary>
public sealed class RecurringJobBootstrapper(
    IServiceScopeFactory scopeFactory,
    IJobCatalog catalog,
    ITaskScheduler scheduler,
    ILogger<RecurringJobBootstrapper> logger) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken) => SyncAsync(cancellationToken);

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task SyncAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITaskConfigRepository>();

        // IHostedLifecycleService.StartingAsync 早于所有 IHostedService.StartAsync，
        // 因此迁移必须在这里先跑完，再同步配置。
        await repository.InitializeAsync(cancellationToken);
        logger.LogInformation("业务库迁移已应用（TaskConfigs）。");

        await repository.SyncDiscoveredAsync(catalog.RecurringJobs, cancellationToken);

        var discoveredIds = catalog.RecurringJobs.Select(job => job.JobId).ToList();
        var removedConfigs = await repository.RemoveMissingAsync(discoveredIds, cancellationToken);
        foreach (var jobId in removedConfigs)
        {
            logger.LogInformation("已从 TaskConfigs 清理废弃任务 {JobId}。", jobId);
        }

        // Hangfire 里可能仍残留已删任务的 recurring（库表已清也不会自动消失）。
        var pruned = scheduler.PruneRecurringExcept(discoveredIds);
        foreach (var jobId in pruned)
        {
            logger.LogInformation("已从 Hangfire 清理残留 recurring {JobId}。", jobId);
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
}