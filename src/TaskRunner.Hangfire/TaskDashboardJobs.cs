using System.ComponentModel;
using Hangfire;
using Hangfire.Community.Dashboard.Forms.Metadata;
using Hangfire.Community.Dashboard.Forms.Support;
using Hangfire.Server;
using Microsoft.Extensions.Logging;
using TaskRunner.Core.Common;
using TaskRunner.Core.Services;

namespace TaskRunner.Hangfire;

[ManagementPage(MenuName = "tasks", Title = "手动调度")]
public sealed class TaskDashboardJobs(
    ITaskScheduler scheduler,
    ITaskConfigRepository configs,
    IRuntimeSettingsStore settings,
    ILogger<TaskDashboardJobs> logger) : IJob
{
    [DisplayName("立即触发任务")]
    [Description("按任务标识把定时任务立即排入队列，由 Worker 执行。")]
    [Queue("default")]
    public async Task Trigger(
        PerformContext context,
        IJobCancellationToken token,
        [DisplayData(Label = "任务标识", Placeholder = "sync-data", Description = "例如 sync-data")] string jobId)
    {
        token?.ThrowIfCancellationRequested();
        var cancellation = token?.ShutdownToken ?? CancellationToken.None;
        await EnsureWritableAsync(cancellation);
        var config = await configs.FindAsync(RequireJobId(jobId), cancellation)
            ?? throw new InvalidOperationException($"任务不存在：{jobId}");
        var queueJobId = scheduler.EnqueueRecurring(config.JobId, config.JobType);
        logger.LogInformation("仪表盘触发任务 {JobId}，队列作业 {QueueJobId}。", config.JobId, queueJobId);
    }

    [DisplayName("更新 Cron 表达式")]
    [Description("更新定时任务的 Cron，并立即作用于 Hangfire。支持 5 位或 6 位（含秒）。")]
    [Queue("default")]
    public async Task UpdateCron(
        PerformContext context,
        IJobCancellationToken token,
        [DisplayData(Label = "任务标识", Placeholder = "sync-data")] string jobId,
        [DisplayData(Label = "Cron 表达式", Placeholder = "*/5 * * * * *", Description = "6 位表达式的第一位是秒")] string cron)
    {
        token?.ThrowIfCancellationRequested();
        var cancellation = token?.ShutdownToken ?? CancellationToken.None;
        await EnsureWritableAsync(cancellation);
        CronExpressionGuard.EnsureValid(cron);
        var id = RequireJobId(jobId);
        var config = await configs.UpdateCronAsync(id, cron, cancellation)
            ?? throw new InvalidOperationException($"任务不存在：{id}");
        if (config.IsEnabled)
        {
            scheduler.AddOrUpdateRecurring(config.JobId, config.JobType, config.CronExpr);
        }

        logger.LogInformation("仪表盘更新任务 {JobId} 的 Cron 为 {Cron}。", config.JobId, config.CronExpr);
    }

    private async Task EnsureWritableAsync(CancellationToken cancellationToken)
    {
        if (await settings.GetReadOnlyModeAsync(cancellationToken))
        {
            throw new InvalidOperationException("当前为只读模式，不能修改任务。");
        }
    }

    private static string RequireJobId(string? jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("任务标识不能为空。", nameof(jobId));
        }

        return jobId.Trim();
    }
}
