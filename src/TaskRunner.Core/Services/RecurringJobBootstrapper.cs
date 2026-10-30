using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskRunner.Core.Common;
using TaskRunner.Core.Models;

namespace TaskRunner.Core.Services;

/// <summary>
/// 在 Hangfire Server StartAsync 之前同步 recurring（IHostedLifecycleService.StartingAsync）。
/// Api 与 Worker 均注册：迁移由会话级 advisory lock 串行化，同步幂等，
/// 因此 Api 故障重启循环中 Worker 也能独立完成「迁移 → 同步配置 → 清理残留」。
/// </summary>
public sealed class RecurringJobBootstrapper(
    IServiceScopeFactory scopeFactory,
    IJobCatalog catalog,
    ITaskScheduler scheduler,
    IOptions<TaskRunnerOptions> options,
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

        // IHostedLifecycleService.StartingAsync 早于所有 IHostedService.StartAsync。
        // 迁移 → 同步 → 清理整体置于会话级 advisory lock：Api/Worker 并发首启时避免
        // 两侧同时读空表再插入相同 JobId 撞唯一索引，令宿主启动失败。
        IReadOnlyList<string> removedConfigs = [];
        IReadOnlyList<TaskConfig> configs = [];
        await repository.RunUnderBootstrapLockAsync(async () =>
        {
            await repository.InitializeAsync(cancellationToken);
            logger.LogInformation("业务库迁移已应用（TaskConfigs）。");

            configs = await repository.SyncDiscoveredAsync(catalog.RecurringJobs, cancellationToken);

            var discoveredIds = catalog.RecurringJobs.Select(job => job.JobId).ToList();
            removedConfigs = await repository.RemoveMissingAsync(discoveredIds, cancellationToken);
        }, cancellationToken);

        foreach (var jobId in removedConfigs)
        {
            logger.LogInformation("已从 TaskConfigs 清理废弃任务 {JobId}。", jobId);
        }

        // Hangfire 里可能仍残留已删任务的 recurring（库表已清也不会自动消失）。
        var pruned = scheduler.PruneRecurringExcept(catalog.RecurringJobs.Select(job => job.JobId).ToList());
        foreach (var jobId in pruned)
        {
            logger.LogInformation("已从 Hangfire 清理残留 recurring {JobId}。", jobId);
        }

        ValidateQueuesCovered();
        WarnHeartbeatCombo();

        var discovered = catalog.RecurringJobs.Select(job => job.JobId).ToHashSet(StringComparer.Ordinal);
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
                    continue;
                }

                scheduler.AddOrUpdateRecurring(config.JobId, config.JobType, config.CronExpr, config.EffectiveQueue);
                logger.LogInformation("定时任务 {JobId} 已注册，Cron={Cron}，Queue={Queue}。",
                    config.JobId, config.CronExpr, config.EffectiveQueue);
            }
            catch (Exception ex)
            {
                // 单个任务注册失败不阻断其余任务；问题配置保留在库，下次启动重试。
                logger.LogError(ex, "定时任务 {JobId} 注册失败。", config.JobId);
            }
        }
    }

    /// <summary>契约声明的队列必须被 Worker 订阅，否则作业将永远滞留 Enqueued（静默丢单）。
    /// 两侧统一经 NormalizeSet 归一化，配置里的空白/大小写差异不产生假告警。</summary>
    private void ValidateQueuesCovered()
    {
        var subscribed = QueueNameGuard.NormalizeSet(options.Value.Queues).ToHashSet(StringComparer.Ordinal);
        var uncovered = catalog.RecurringJobs
            .Select(job => QueueNameGuard.Normalize(job.Queue))
            .Distinct(StringComparer.Ordinal)
            .Where(queue => !subscribed.Contains(queue))
            .ToList();
        if (uncovered.Count > 0)
        {
            logger.LogError(
                "任务声明了未被订阅的队列 {Queues}（Hangfire:Queues={Configured}）。这些作业将滞留 Enqueued，请在 Worker 配置中订阅。",
                string.Join(", ", uncovered), string.Join(", ", options.Value.Queues));
        }
    }

    /// <summary>配了死信 ping 却关了心跳，外部告警永远不会生效——启动时明确警告。</summary>
    private void WarnHeartbeatCombo()
    {
        if (!string.IsNullOrWhiteSpace(options.Value.HeartbeatPingUrl) && !options.Value.HeartbeatEnabled)
        {
            logger.LogWarning("已配置 Hangfire:HeartbeatPingUrl 但 HeartbeatEnabled=false：死信告警不会生效。");
        }
    }
}
