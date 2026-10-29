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
[Authorize(Policy = AdminAuthDefaults.Policy)]
[Tags("任务管理")]
public sealed class TasksController(
    ITaskConfigRepository repository,
    ITaskScheduler scheduler,
    HangfireMonitoringReader monitoring) : ControllerBase
{
    [HttpGet(Name = "ListTasks")]
    [EndpointSummary("列表")]
    [EndpointDescription("page/pageSize 分页（pageSize=0 返回全量）；支持 If-None-Match 缓存协商（命中返回 304）。")]
    [ProducesResponseType(typeof(IReadOnlyList<TaskConfigView>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    public async Task<ActionResult<IReadOnlyList<TaskConfigView>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 0,
        CancellationToken cancellationToken = default)
    {
        var configs = await repository.ListAsync(cancellationToken);
        var views = monitoring.ReadTasks(configs);

        // 弱 ETag：数量 + 最大 UpdatedAt 指纹；命中 If-None-Match 返回 304
        var maxUpdated = configs.Count == 0 ? DateTime.MinValue : configs.Max(item => item.UpdatedAt);
        var etag = $"W/\"{configs.Count:x8}-{maxUpdated:yyyyMMddHHmmss}\"";
        if (Request.Headers.IfNoneMatch.ToString()
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Contains(etag, StringComparer.Ordinal))
        {
            Response.Headers.ETag = etag;
            return StatusCode(StatusCodes.Status304NotModified);
        }

        Response.Headers.ETag = etag;
        Response.Headers["X-Total-Count"] = views.Count.ToString();

        if (pageSize > 0)
        {
            page = Math.Max(1, page);
            views = views.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        }

        return Ok(views);
    }

    [HttpGet("statistics", Name = "GetTaskStatistics")]
    [EndpointSummary("统计")]
    [EndpointDescription("成功 / 失败 / 排队 / 处理中。")]
    public ActionResult<JobStatistics> Statistics() => Ok(monitoring.ReadStatistics());

    [HttpGet("{jobId}", Name = "GetTask")]
    [EndpointSummary("详情")]
    [ProducesResponseType(typeof(TaskConfigView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TaskConfigView>> Get(string jobId, CancellationToken cancellationToken)
    {
        var config = await repository.FindAsync(jobId, cancellationToken);
        if (config is null) { return TaskNotFound(jobId); }
        return Ok(monitoring.ReadOne(config));
    }

    [HttpGet("{jobId}/history", Name = "GetTaskHistory")]
    [EndpointSummary("历史")]
    public async Task<ActionResult<IReadOnlyList<JobHistoryItem>>> History(
        string jobId,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var config = await repository.FindAsync(jobId, cancellationToken);
        if (config is null) { return TaskNotFound(jobId); }
        return Ok(monitoring.ReadHistory(jobId, limit));
    }

    [HttpPost("{jobId}/trigger", Name = "TriggerTask")]
    [EndpointSummary("触发")]
    [EndpointDescription("立即执行，或 body 传 runAt / delaySeconds 预约一次。异步受理：返回 202，执行结果见作业历史。")]
    [ProducesResponseType(typeof(QueueJobResult), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<QueueJobResult>> Trigger(
        string jobId,
        [FromBody] TriggerRequest? request,
        CancellationToken cancellationToken)
    {
        var config = await repository.FindAsync(jobId, cancellationToken);
        if (config is null) { return TaskNotFound(jobId); }
        if (!config.IsEnabled)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "任务已暂停",
                detail: $"任务 {config.JobId} 已暂停，请先恢复再触发。");
        }

        var queue = config.EffectiveQueue;
        if (request?.RunAt is null && request?.DelaySeconds is null)
        {
            var queueJobId = scheduler.Enqueue(config.JobId, config.JobType, queue);
            return AcceptedAtAction(nameof(Get), new { jobId = config.JobId },
                new QueueJobResult(config.JobId, queueJobId));
        }

        if (request.RunAt is not null && request.DelaySeconds is not null)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "参数冲突",
                detail: "runAt 与 delaySeconds 不能同时指定。");
        }

        DateTimeOffset runAt;
        if (request.RunAt is not null)
        {
            runAt = request.RunAt.Value;
        }
        else
        {
            if (request.DelaySeconds is null or <= 0)
            {
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "参数无效",
                    detail: "delaySeconds 必须为正整数。");
            }

            runAt = DateTimeOffset.UtcNow.AddSeconds(request.DelaySeconds.Value);
        }

        if (runAt <= DateTimeOffset.UtcNow)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "参数无效",
                detail: "runAt 必须是未来时刻。");
        }

        var scheduledId = scheduler.Schedule(config.JobId, config.JobType, queue, runAt);
        return AcceptedAtAction(nameof(Get), new { jobId = config.JobId },
            new QueueJobResult(config.JobId, scheduledId, Scheduled: true));
    }

    [HttpPost("{jobId}/pause", Name = "PauseTask")]
    [EndpointSummary("暂停")]
    public async Task<ActionResult<TaskConfigView>> Pause(string jobId, CancellationToken cancellationToken)
    {
        var config = await repository.SetEnabledAsync(jobId, false, cancellationToken);
        if (config is null) { return TaskNotFound(jobId); }
        scheduler.RemoveRecurring(config.JobId);
        return Ok(monitoring.ReadOne(config));
    }

    [HttpPost("{jobId}/resume", Name = "ResumeTask")]
    [EndpointSummary("恢复")]
    public async Task<ActionResult<TaskConfigView>> Resume(string jobId, CancellationToken cancellationToken)
    {
        var config = await repository.FindAsync(jobId, cancellationToken);
        if (config is null) { return TaskNotFound(jobId); }
        try { CronExpressionGuard.EnsureValid(config.CronExpr); }
        catch (ArgumentException ex) { return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Cron 无效", detail: ex.Message); }
        config = await repository.SetEnabledAsync(jobId, true, cancellationToken);
        scheduler.AddOrUpdateRecurring(config!.JobId, config.JobType, config.CronExpr, config.Queue);
        return Ok(monitoring.ReadOne(config));
    }

    [Authorize(Policy = AdminAuthDefaults.Policy)]
    [HttpPut("{jobId}/cron", Name = "UpdateTaskCron")]
    [EndpointSummary("改 Cron")]
    public async Task<ActionResult<TaskConfigView>> UpdateCron(string jobId, UpdateCronRequest request, CancellationToken cancellationToken)
    {
        try { CronExpressionGuard.EnsureValid(request.Cron); }
        catch (ArgumentException ex) { return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Cron 无效", detail: ex.Message); }
        var config = await repository.UpdateCronAsync(jobId, request.Cron, cancellationToken);
        if (config is null) { return TaskNotFound(jobId); }
        if (config.IsEnabled) { scheduler.AddOrUpdateRecurring(config.JobId, config.JobType, config.CronExpr, config.EffectiveQueue); }
        return Ok(monitoring.ReadOne(config));
    }

    private ActionResult TaskNotFound(string jobId) =>
        Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "任务不存在",
            detail: $"任务不存在：{jobId}");
}
