using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskRunner.Api.Auth;
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
    HangfireMonitoringReader monitoring,
    IRuntimeSettingsStore settings) : ControllerBase
{
    [HttpGet(Name = "ListTasks")]
    [EndpointSummary("获取定时任务列表")]
    public async Task<ActionResult<IReadOnlyList<TaskConfigView>>> List(CancellationToken cancellationToken)
    {
        var configs = await repository.ListAsync(cancellationToken);
        return Ok(monitoring.ReadTasks(configs));
    }

    [HttpGet("statistics", Name = "GetTaskStatistics")]
    [EndpointSummary("获取任务统计")]
    [EndpointDescription("返回成功、失败、排队和处理中的数量，数据来自 Hangfire。")]
    public ActionResult<JobStatistics> Statistics() => Ok(monitoring.ReadStatistics());

    [HttpGet("settings/readonly", Name = "GetReadOnlyMode")]
    [EndpointSummary("获取只读模式（共享库）")]
    public async Task<ActionResult<object>> GetReadOnly(CancellationToken cancellationToken)
        => Ok(new { readOnly = await settings.GetReadOnlyModeAsync(cancellationToken) });

    [Authorize(Policy = AdminAuthDefaults.Policy)]
    [HttpPut("settings/readonly", Name = "SetReadOnlyMode")]
    [EndpointSummary("设置只读模式（写入共享库，Api/Worker 同时生效）")]
    public async Task<ActionResult<object>> SetReadOnly(SetReadOnlyRequest request, CancellationToken cancellationToken)
    {
        await settings.SetReadOnlyModeAsync(request.ReadOnly, cancellationToken);
        return Ok(new { readOnly = request.ReadOnly });
    }

    [HttpGet("{jobId}", Name = "GetTask")]
    [EndpointSummary("获取单个定时任务")]
    public async Task<ActionResult<TaskConfigView>> Get(string jobId, CancellationToken cancellationToken)
    {
        var config = await repository.FindAsync(jobId, cancellationToken);
        if (config is null) { return NotFound(new { message = $"任务不存在：{jobId}" }); }
        return Ok(monitoring.ReadTasks([config])[0]);
    }

    [HttpGet("{jobId}/history", Name = "GetTaskHistory")]
    [EndpointSummary("查看任务执行历史")]
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
    [EndpointSummary("手动触发指定任务")]
    public async Task<ActionResult<QueueJobResult>> Trigger(string jobId, CancellationToken cancellationToken)
    {
        var config = await repository.FindAsync(jobId, cancellationToken);
        if (config is null) { return NotFound(new { message = $"任务不存在：{jobId}" }); }
        var queueJobId = scheduler.EnqueueRecurring(config.JobId, config.JobType);
        return Ok(new QueueJobResult(config.JobId, queueJobId));
    }

    [Authorize(Policy = AdminAuthDefaults.Policy)]
    [HttpPost("{jobId}/pause", Name = "PauseTask")]
    [EndpointSummary("暂停定时任务")]
    public async Task<ActionResult<TaskConfigView>> Pause(string jobId, CancellationToken cancellationToken)
    {
        var config = await repository.SetEnabledAsync(jobId, false, cancellationToken);
        if (config is null) { return NotFound(new { message = $"任务不存在：{jobId}" }); }
        scheduler.RemoveRecurring(config.JobId);
        return Ok(monitoring.ReadTasks([config])[0]);
    }

    [Authorize(Policy = AdminAuthDefaults.Policy)]
    [HttpPost("{jobId}/resume", Name = "ResumeTask")]
    [EndpointSummary("恢复定时任务")]
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
    [EndpointSummary("更新 Cron 表达式")]
    public async Task<ActionResult<TaskConfigView>> UpdateCron(string jobId, UpdateCronRequest request, CancellationToken cancellationToken)
    {
        try { CronExpressionGuard.EnsureValid(request.Cron); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        var config = await repository.UpdateCronAsync(jobId, request.Cron, cancellationToken);
        if (config is null) { return NotFound(new { message = $"任务不存在：{jobId}" }); }
        if (config.IsEnabled) { scheduler.AddOrUpdateRecurring(config.JobId, config.JobType, config.CronExpr); }
        return Ok(monitoring.ReadTasks([config])[0]);
    }

    [Authorize(Policy = AdminAuthDefaults.Policy)]
    [HttpPut("{jobId}/parameters", Name = "UpdateTaskParameters")]
    [EndpointSummary("更新任务 JSON 参数（预留扩展）")]
    public async Task<ActionResult<TaskConfigView>> UpdateParameters(
        string jobId,
        UpdateParametersRequest request,
        CancellationToken cancellationToken)
    {
        var config = await repository.UpdateParametersAsync(jobId, request.Parameters, cancellationToken);
        if (config is null) { return NotFound(new { message = $"任务不存在：{jobId}" }); }
        return Ok(monitoring.ReadTasks([config])[0]);
    }
}
