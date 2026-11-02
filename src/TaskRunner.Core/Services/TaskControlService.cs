using TaskRunner.Core.Common;
using TaskRunner.Core.Models;

namespace TaskRunner.Core.Services;

/// <summary>手动触发后的结果状态，供 REST / 运行台各自映射成 404/409 或闪存文案。</summary>
public enum TriggerStatus
{
    NotFound,
    Paused,
    Accepted
}

/// <param name="Status">结果状态。</param>
/// <param name="Config">任务配置（未找到时为 null）。</param>
/// <param name="QueueJobId">Hangfire 队列任务标识（被接受时非空）。</param>
/// <param name="Scheduled">true = 已预约；false = 立即入队。</param>
public sealed record TriggerResult(TriggerStatus Status, TaskConfig? Config, string? QueueJobId, bool Scheduled);

/// <summary>
/// 任务控制用例：暂停 / 恢复 / 手动触发。REST 与运行台共用同一套校验与执行顺序，
/// 避免两条通道各自实现导致行为漂移（如恢复时队列取值不一致）。
/// 只负责"配置 + 调度"的编排，不关心各自通道的响应格式。
/// </summary>
public sealed class TaskControlService(ITaskConfigRepository repository, ITaskScheduler scheduler)
{
    /// <summary>暂停：置禁用并从 Hangfire 移除 recurring；任务不存在返回 null。</summary>
    public async Task<TaskConfig?> PauseAsync(string jobId, CancellationToken cancellationToken)
    {
        var config = await repository.SetEnabledAsync(jobId, enabled: false, cancellationToken);
        if (config is null)
        {
            return null;
        }

        scheduler.RemoveRecurring(config.JobId);
        return config;
    }

    /// <summary>
    /// 恢复：先校验 Cron（脏数据抛 <see cref="ArgumentException"/>，由调用方映射为 400/错误文案），
    /// 置启用后重新注册 recurring；任务不存在返回 null。
    /// </summary>
    public async Task<TaskConfig?> ResumeAsync(string jobId, CancellationToken cancellationToken)
    {
        var config = await repository.FindAsync(jobId, cancellationToken);
        if (config is null)
        {
            return null;
        }

        CronExpressionGuard.EnsureValid(config.CronExpr);
        config = await repository.UpdateAsync(config, row => row.IsEnabled = true, cancellationToken);
        scheduler.AddOrUpdateRecurring(config!.JobId, config.JobType, config.CronExpr, config.EffectiveQueue);
        return config;
    }

    /// <summary>
    /// 改 Cron：先校验表达式（非法抛 <see cref="ArgumentException"/>），更新配置；
    /// 任务处于启用态时按新 Cron 重新注册 recurring（暂停任务只保存，恢复时再注册）。
    /// 任务不存在返回 null。
    /// </summary>
    public async Task<TaskConfig?> UpdateCronAsync(string jobId, string cron, CancellationToken cancellationToken)
    {
        CronExpressionGuard.EnsureValid(cron);
        var config = await repository.UpdateCronAsync(jobId, cron, cancellationToken);
        if (config is null)
        {
            return null;
        }

        if (config.IsEnabled)
        {
            scheduler.AddOrUpdateRecurring(config.JobId, config.JobType, config.CronExpr, config.EffectiveQueue);
        }

        return config;
    }

    /// <summary>
    /// 手动触发：runAtUtc 为 null 立即入队，否则预约到该时刻。
    /// 任务不存在 / 已暂停时不会产生任何 Hangfire 作业。
    /// </summary>
    public async Task<TriggerResult> TriggerAsync(string jobId, DateTimeOffset? runAtUtc, CancellationToken cancellationToken)
    {
        var config = await repository.FindAsync(jobId, cancellationToken);
        if (config is null)
        {
            return new TriggerResult(TriggerStatus.NotFound, null, null, Scheduled: false);
        }

        if (!config.IsEnabled)
        {
            return new TriggerResult(TriggerStatus.Paused, config, null, Scheduled: false);
        }

        var scheduled = runAtUtc is not null;
        var queueJobId = scheduled
            ? scheduler.Schedule(jobId, config.JobType, config.EffectiveQueue, runAtUtc!.Value)
            : scheduler.Enqueue(jobId, config.JobType, config.EffectiveQueue);
        return new TriggerResult(TriggerStatus.Accepted, config, queueJobId, scheduled);
    }
}
