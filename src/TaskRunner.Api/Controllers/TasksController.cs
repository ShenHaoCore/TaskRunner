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
    HangfireMonitoringReader monitoring,
    TaskControlService control) : ControllerBase
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

        var etag = ComputeETag(views, configs, page, pageSize);

        if (Request.Headers.IfNoneMatch.ToString()
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Contains(etag, StringComparer.Ordinal))
        {
            Response.Headers.ETag = etag;
            Response.Headers["X-Total-Count"] = views.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return StatusCode(StatusCodes.Status304NotModified);
        }

        Response.Headers.ETag = etag;
        Response.Headers["X-Total-Count"] = views.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);

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
        if (request?.RunAt is not null && request?.DelaySeconds is not null)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "参数冲突",
                detail: "runAt 与 delaySeconds 不能同时指定。");
        }

        DateTimeOffset? runAt = request?.RunAt;
        if (runAt is null && request?.DelaySeconds is not null)
        {
            if (request.DelaySeconds <= 0)
            {
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "参数无效",
                    detail: "delaySeconds 必须为正整数。");
            }

            runAt = DateTimeOffset.UtcNow.AddSeconds(request.DelaySeconds.Value);
        }

        if (runAt is not null && runAt <= DateTimeOffset.UtcNow)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "参数无效",
                detail: "runAt 必须是未来时刻。");
        }

        var result = await control.TriggerAsync(jobId, runAt, cancellationToken);
        return result.Status switch
        {
            TriggerStatus.NotFound => TaskNotFound(jobId),
            TriggerStatus.Paused => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "任务已暂停",
                detail: $"任务 {jobId} 已暂停，请先恢复再触发。"),
            _ => AcceptedAtAction(nameof(Get), new { jobId },
                new QueueJobResult(jobId, result.QueueJobId!, result.Scheduled))
        };
    }

    [HttpPost("{jobId}/pause", Name = "PauseTask")]
    [EndpointSummary("暂停")]
    public async Task<ActionResult<TaskConfigView>> Pause(string jobId, CancellationToken cancellationToken)
    {
        var config = await control.PauseAsync(jobId, cancellationToken);
        if (config is null) { return TaskNotFound(jobId); }
        return Ok(monitoring.ReadOne(config));
    }

    [HttpPost("{jobId}/resume", Name = "ResumeTask")]
    [EndpointSummary("恢复")]
    public async Task<ActionResult<TaskConfigView>> Resume(string jobId, CancellationToken cancellationToken)
    {
        TaskConfig? config;
        try
        {
            config = await control.ResumeAsync(jobId, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Cron 无效", detail: ex.Message);
        }

        if (config is null) { return TaskNotFound(jobId); }
        return Ok(monitoring.ReadOne(config));
    }

    [Authorize(Policy = AdminAuthDefaults.Policy)]
    [HttpPut("{jobId}/cron", Name = "UpdateTaskCron")]
    [EndpointSummary("改 Cron")]
    public async Task<ActionResult<TaskConfigView>> UpdateCron(string jobId, UpdateCronRequest request, CancellationToken cancellationToken)
    {
        TaskConfig? config;
        try
        {
            config = await control.UpdateCronAsync(jobId, request.Cron, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Cron 无效", detail: ex.Message);
        }

        if (config is null) { return TaskNotFound(jobId); }
        return Ok(monitoring.ReadOne(config));
    }

    /// <summary>
    /// 列表表示的 ETag：配置版本（UpdatedAt.Ticks）+ 运行态（上次/下次执行、最近状态）+ 分页参数。
    /// 只哈希配置会让任务每次执行后 ETag 不变，客户端 If-None-Match 永远拿到 304 和过期的运行状态；
    /// 不同分页是不同表示，必须各自持有 ETag。
    /// </summary>
    private static string ComputeETag(
        IReadOnlyList<TaskConfigView> views,
        IReadOnlyList<TaskConfig> configs,
        int page,
        int pageSize)
    {
        var configTicks = configs.ToDictionary(item => item.JobId, item => item.UpdatedAt.Ticks, StringComparer.Ordinal);
        var fingerprint = string.Join(";",
            views.OrderBy(item => item.JobId, StringComparer.Ordinal).Select(item =>
                $"{item.JobId}:{configTicks[item.JobId]}:{item.LastExecution?.Ticks}:{item.NextExecution?.Ticks}:{item.LastJobState}")) +
            $"|{page}:{pageSize}";
        return $"W/\"{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(fingerprint)))[..16]}\"";
    }

    private ObjectResult TaskNotFound(string jobId) =>
        Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "任务不存在",
            detail: $"任务不存在：{jobId}");
}
