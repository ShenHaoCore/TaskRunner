using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TaskRunner.Api.Auth;
using TaskRunner.Api.Services;
using TaskRunner.Core.Common;
using TaskRunner.Core.Models;
using TaskRunner.Core.Services;

namespace TaskRunner.Api.Controllers;

/// <summary>任务运行台（MVC）：服务端过滤、触发、行内改 Cron 与批量操作，需管理员会话。</summary>
[Authorize(Policy = AdminAuthDefaults.Policy)]
[Route("run")]
public sealed class RunController(
    ITaskConfigRepository repository,
    HangfireMonitoringReader monitoring,
    TaskControlService control,
    IOptions<TaskRunnerOptions> options) : Controller
{
    [HttpGet("/run")]
    public async Task<IActionResult> Index(string? q, string? queue, CancellationToken cancellationToken)
    {
        ViewData["Title"] = "任务运行台 — TaskRunner";
        ViewData["NavActive"] = "run";
        ViewData["AutoRefresh"] = "30";

        var configs = await repository.ListAsync(cancellationToken);
        var tasks = monitoring.ReadTasks(configs);

        // 队列下拉用全量集合（过滤后仍能切换到其他队列）
        var queues = tasks.Select(t => t.Queue).Distinct(StringComparer.Ordinal).OrderBy(q => q, StringComparer.Ordinal).ToList();

        var query = q?.Trim();
        if (!string.IsNullOrEmpty(query))
        {
            tasks = tasks
                .Where(t => t.JobId.Contains(query, StringComparison.OrdinalIgnoreCase)
                            || (t.JobName ?? "").Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var queueName = queue?.Trim();
        if (!string.IsNullOrEmpty(queueName))
        {
            tasks = tasks.Where(t => string.Equals(t.Queue, queueName, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return View(new RunIndexViewModel(tasks, query ?? "", queueName ?? "", queues, options.Value.ReadOnlyMode));
    }

    [HttpPost("{jobId}/trigger")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Trigger(string jobId, CancellationToken cancellationToken)
    {
        var result = await control.TriggerAsync(jobId, runAtUtc: null, cancellationToken);
        switch (result.Status)
        {
            case TriggerStatus.NotFound:
                TempData.SetFlashError($"任务不存在：{jobId}");
                break;
            case TriggerStatus.Paused:
                TempData.SetFlashError("任务已暂停，请先在任务列表或 Dashboard 恢复后再触发。");
                break;
            default:
                TempData.SetFlashSuccess($"已入队 {jobId} → {result.QueueJobId}");
                break;
        }

        return RedirectToAction(nameof(Index));
    }

    /// <summary>行内改 Cron（运行台页面通道）：与 REST UpdateCron 共用 TaskControlService，PRG + flash 反馈。</summary>
    [HttpPost("{jobId}/cron")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateCron(string jobId, string? cron, CancellationToken cancellationToken)
    {
        TaskConfig? updated;
        try
        {
            updated = await control.UpdateCronAsync(jobId, cron?.Trim() ?? "", cancellationToken);
        }
        catch (ArgumentException ex)
        {
            TempData.SetFlashError($"Cron 无效：{ex.Message}");
            return RedirectToAction(nameof(Index));
        }

        if (updated is null)
        {
            TempData.SetFlashError($"任务不存在：{jobId}");
            return RedirectToAction(nameof(Index));
        }

        TempData.SetFlashSuccess($"已更新 {updated.JobId} 的 Cron：{updated.CronExpr}");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>批量操作（勾选 + 工具条）：逐个复用单任务语义，PRG flash 汇总成败。</summary>
    [HttpPost("bulk")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Bulk(string? action, [FromForm] List<string>? jobIds, CancellationToken cancellationToken)
    {
        var ids = jobIds?.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList() ?? [];
        if (ids.Count == 0)
        {
            TempData.SetFlashError("未选择任务。");
            return RedirectToAction(nameof(Index));
        }

        // 操作名与处理器只映射一次：循环内不再重复 switch，新增操作时也不会两处漏改。
        var (actionName, operation) = action switch
        {
            "pause" => ("暂停", (Func<string, CancellationToken, Task<string?>>)PauseOneAsync),
            "resume" => ("恢复", ResumeOneAsync),
            "trigger" => ("触发", TriggerOneAsync),
            _ => (null, null)
        };
        if (operation is null)
        {
            TempData.SetFlashError("未知操作。");
            return RedirectToAction(nameof(Index));
        }

        var succeeded = 0;
        var errors = new List<string>();
        foreach (var jobId in ids)
        {
            try
            {
                var error = await operation(jobId, cancellationToken);
                if (error is null)
                {
                    succeeded++;
                }
                else
                {
                    errors.Add(error);
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{jobId}: {ex.Message}");
            }
        }

        if (errors.Count > 0)
        {
            var summary = string.Join("；", errors.Take(3)) + (errors.Count > 3 ? $" 等 {errors.Count} 条" : "");
            TempData.SetFlashError($"{actionName}完成：成功 {succeeded}，失败 {errors.Count}（{summary}）");
        }
        else
        {
            TempData.SetFlashSuccess($"{actionName}完成：成功 {succeeded}。");
        }

        return RedirectToAction(nameof(Index));
    }

    /// <summary>3.5.0 及更早的静态页书签重定向。</summary>
    [HttpGet("/run.html")]
    public IActionResult LegacyRun() => RedirectPermanent("/run");

    /// <returns>null 表示成功，否则为错误描述。</returns>
    private async Task<string?> PauseOneAsync(string jobId, CancellationToken cancellationToken)
        => await control.PauseAsync(jobId, cancellationToken) is null ? $"{jobId} 不存在" : null;

    /// <returns>null 表示成功，否则为错误描述。Cron 无效由服务抛出，统一走 Bulk 的异常汇总。</returns>
    private async Task<string?> ResumeOneAsync(string jobId, CancellationToken cancellationToken)
        => await control.ResumeAsync(jobId, cancellationToken) is null ? $"{jobId} 不存在" : null;

    /// <returns>null 表示成功，否则为错误描述。</returns>
    private async Task<string?> TriggerOneAsync(string jobId, CancellationToken cancellationToken)
    {
        var result = await control.TriggerAsync(jobId, runAtUtc: null, cancellationToken);
        return result.Status switch
        {
            TriggerStatus.NotFound => $"{jobId} 不存在",
            TriggerStatus.Paused => $"{jobId} 已暂停",
            _ => null
        };
    }

    public sealed record RunIndexViewModel(
        IReadOnlyList<TaskConfigView> Tasks,
        string Query,
        string Queue,
        IReadOnlyList<string> Queues,
        bool ReadOnly);
}
