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

        // 弱 ETag：数量 + 最大 UpdatedAt 指纹。先判再读监控数据：命中 If-None-Match 时
        // Hangfire 存储的全量读取（最贵的一步）整体跳过。
        var maxUpdated = configs.Count == 0 ? DateTime.MinValue : configs.Max(item => item.UpdatedAt);
        var etag = $"W/\"{configs.Count:x8}-{maxUpdated:yyyyMMddHHmmss}\"";
        if (Request.Headers.IfNoneMatch.ToString()
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Contains(etag, StringComparer.Ordinal))
        {
            Response.Headers.ETag = etag;
            return StatusCode(StatusCodes.Status304NotModified);
        }

        var views = monitoring.ReadTasks(configs);
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

    [HttpGet("cron-preview", Name = "PreviewCron")]
    [EndpointSummary("Cron 预览")]
    [EndpointDescription("解析 Cron 表达式并返回未来 5 次执行（UTC ISO）。供运行台行内编辑保存前预览，解析规则与启停/改 Cron 完全一致。")]
    [ProducesResponseType(typeof(CronPreviewView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public IActionResult PreviewCron([FromQuery] string? expr)
    {
        try
        {
            return Ok(new CronPreviewView(CronExpressionGuard.NextOccurrences(expr, 5)));
        }
        catch (ArgumentException ex)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Cron 无效", detail: ex.Message);
        }
    }

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
        if (InvalidCron(config.CronExpr) is { } invalid) { return invalid; }

        // 复用已加载实体更新，避免 SetEnabledAsync 内部再按 JobId 重查同一行。
        config = await repository.UpdateAsync(config, row => row.IsEnabled = true, cancellationToken);
        scheduler.AddOrUpdateRecurring(config!.JobId, config.JobType, config.CronExpr, config.Queue);
        return Ok(monitoring.ReadOne(config));
    }

    [Authorize(Policy = AdminAuthDefaults.Policy)]
    [HttpPut("{jobId}/cron", Name = "UpdateTaskCron")]
    [EndpointSummary("改 Cron")]
    public async Task<ActionResult<TaskConfigView>> UpdateCron(string jobId, UpdateCronRequest request, CancellationToken cancellationToken)
    {
        if (InvalidCron(request.Cron) is { } invalid) { return invalid; }
        var config = await repository.UpdateCronAsync(jobId, request.Cron, cancellationToken);
        if (config is null) { return TaskNotFound(jobId); }
        if (config.IsEnabled) { scheduler.AddOrUpdateRecurring(config.JobId, config.JobType, config.CronExpr, config.EffectiveQueue); }
        return Ok(monitoring.ReadOne(config));
    }

    /// <summary>Cron 非法时返回 400 Problem，否则 null——写入口统一走这里，新增端点不易漏校验。</summary>
    private ActionResult? InvalidCron(string cron)
    {
        try
        {
            CronExpressionGuard.EnsureValid(cron);
            return null;
        }
        catch (ArgumentException ex)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Cron 无效", detail: ex.Message);
        }
    }

    private ActionResult TaskNotFound(string jobId) =>
        Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "任务不存在",
            detail: $"任务不存在：{jobId}");
}
