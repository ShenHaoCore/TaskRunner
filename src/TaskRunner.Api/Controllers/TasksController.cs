using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskRunner.Api.Auth;
using TaskRunner.Api.Contracts;
using TaskRunner.Api.Services;
using TaskRunner.Core.Common;
using TaskRunner.Core.Models;
using TaskRunner.Core.Services;

namespace TaskRunner.Api.Controllers;

[ApiController]
[Route("api/tasks")]
[Tags("任务管理")]
public sealed class TasksController(
    ITaskConfigRepository repository,
    ITaskScheduler scheduler,
    HangfireMonitoringReader monitoring) : ControllerBase
{
    [HttpGet(Name = "ListTasks")]
    [EndpointSummary("列表")]
    public async Task<ActionResult<IReadOnlyList<TaskConfigView>>> List(CancellationToken cancellationToken)
    {
        var configs = await repository.ListAsync(cancellationToken);
        return Ok(monitoring.ReadTasks(configs));
    }

    [HttpGet("statistics", Name = "GetTaskStatistics")]
    [EndpointSummary("统计")]
    [EndpointDescription("成功 / 失败 / 排队 / 处理中。")]
    public ActionResult<JobStatistics> Statistics() => Ok(monitoring.ReadStatistics());

    [HttpGet("{jobId}", Name = "GetTask")]
    [EndpointSummary("详情")]
    public async Task<ActionResult<TaskConfigView>> Get(string jobId, CancellationToken cancellationToken)
    {
        var config = await repository.FindAsync(jobId, cancellationToken);
        if (config is null) { return NotFound(new { message = $"任务不存在：{jobId}" }); }
        return Ok(monitoring.ReadTasks([config])[0]);
    }

    [HttpGet("{jobId}/history", Name = "GetTaskHistory")]
    [EndpointSummary("历史")]
    public async Task<ActionResult<IReadOnlyList<JobHistoryItem>>> History(
        string jobId,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var config = await repository.FindAsync(jobId, cancellationToken);
        if (config is null) { return NotFound(new { message = $"任务不存在：{jobId}" }); }
        return Ok(monitoring.ReadHistory(jobId, limit));
    }

    [Authorize(Policy = AdminAuthDefaults.Policy)]
    [HttpPost("{jobId}/trigger", Name = "TriggerTask")]
    [EndpointSummary("触发")]
    public async Task<ActionResult<QueueJobResult>> Trigger(string jobId, CancellationToken cancellationToken)
    {
        var config = await repository.FindAsync(jobId, cancellationToken);
        if (config is null) { return NotFound(new { message = $"任务不存在：{jobId}" }); }
        var queueJobId = scheduler.Enqueue(config.JobId, config.JobType);
        return Ok(new QueueJobResult(config.JobId, queueJobId));
    }

    [Authorize(Policy = AdminAuthDefaults.Policy)]
    [HttpPost("{jobId}/pause", Name = "PauseTask")]
    [EndpointSummary("暂停")]
    public async Task<ActionResult<TaskConfigView>> Pause(string jobId, CancellationToken cancellationToken)
    {
        var config = await repository.SetEnabledAsync(jobId, false, cancellationToken);
        if (config is null) { return NotFound(new { message = $"任务不存在：{jobId}" }); }
        scheduler.RemoveRecurring(config.JobId);
        return Ok(monitoring.ReadTasks([config])[0]);
    }

    [Authorize(Policy = AdminAuthDefaults.Policy)]
    [HttpPost("{jobId}/resume", Name = "ResumeTask")]
    [EndpointSummary("恢复")]
    public async Task<ActionResult<TaskConfigView>> Resume(string jobId, CancellationToken cancellationToken)
    {
        var config = await repository.FindAsync(jobId, cancellationToken);
        if (config is null) { return NotFound(new { message = $"任务不存在：{jobId}" }); }
        try { CronExpressionGuard.EnsureValid(config.CronExpr); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        config = await repository.SetEnabledAsync(jobId, true, cancellationToken);
        scheduler.AddOrUpdateRecurring(config!.JobId, config.JobType, config.CronExpr);
        return Ok(monitoring.ReadTasks([config])[0]);
    }

    [Authorize(Policy = AdminAuthDefaults.Policy)]
    [HttpPut("{jobId}/cron", Name = "UpdateTaskCron")]
    [EndpointSummary("改 Cron")]
    public async Task<ActionResult<TaskConfigView>> UpdateCron(string jobId, UpdateCronRequest request, CancellationToken cancellationToken)
    {
        try { CronExpressionGuard.EnsureValid(request.Cron); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        var config = await repository.UpdateCronAsync(jobId, request.Cron, cancellationToken);
        if (config is null) { return NotFound(new { message = $"任务不存在：{jobId}" }); }
        if (config.IsEnabled) { scheduler.AddOrUpdateRecurring(config.JobId, config.JobType, config.CronExpr); }
        return Ok(monitoring.ReadTasks([config])[0]);
    }
}
