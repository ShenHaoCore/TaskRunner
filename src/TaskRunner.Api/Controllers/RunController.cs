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
    ITaskScheduler scheduler,
    HangfireMonitoringReader monitoring,
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
        var config = await repository.FindAsync(jobId, cancellationToken);
        if (config is null)
        {
            TempData.SetFlashError($"任务不存在：{jobId}");
            return RedirectToAction(nameof(Index));
        }

        if (!config.IsEnabled)
        {
            TempData.SetFlashError("任务已暂停，请先在任务列表或 Dashboard 恢复后再触发。");
            return RedirectToAction(nameof(Index));
        }

        var queueJobId = scheduler.Enqueue(config.JobId, config.JobType, config.EffectiveQueue);
        TempData.SetFlashSuccess($"已入队 {config.JobId} → {queueJobId}");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>行内改 Cron（运行台页面通道）：与 REST UpdateCron 同构，PRG + flash 反馈。</summary>
    [HttpPost("{jobId}/cron")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateCron(string jobId, string? cron, CancellationToken cancellationToken)
    {
        var trimmed = cron?.Trim() ?? "";
        try
        {
            CronExpressionGuard.EnsureValid(trimmed);
        }
        catch (ArgumentException ex)
        {
            TempData.SetFlashError($"Cron 无效：{ex.Message}");
            return RedirectToAction(nameof(Index));
        }

        var config = await repository.FindAsync(jobId, cancellationToken);
        if (config is null)
        {
            TempData.SetFlashError($"任务不存在：{jobId}");
            return RedirectToAction(nameof(Index));
        }

        var updated = await repository.UpdateCronAsync(jobId, trimmed, cancellationToken);
        if (updated is null)
        {
            TempData.SetFlashError($"任务不存在：{jobId}");
            return RedirectToAction(nameof(Index));
        }

        // 暂停任务允许先改 Cron（保存不注册），恢复时按新 Cron 注册
        if (updated.IsEnabled)
        {
            scheduler.AddOrUpdateRecurring(updated.JobId, updated.JobType, updated.CronExpr, updated.EffectiveQueue);
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

        var actionName = action switch
        {
            "pause" => "暂停",
            "resume" => "恢复",
            "trigger" => "触发",
            _ => null
        };
        if (actionName is null)
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
                var error = action switch
                {
                    "pause" => await PauseOneAsync(jobId, cancellationToken),
                    "resume" => await ResumeOneAsync(jobId, cancellationToken),
                    _ => await TriggerOneAsync(jobId, cancellationToken)
                };
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
    {
        var paused = await repository.SetEnabledAsync(jobId, false, cancellationToken);
        if (paused is null)
        {
            return $"{jobId} 不存在";
        }

        scheduler.RemoveRecurring(jobId);
        return null;
    }

    /// <returns>null 表示成功，否则为错误描述。恢复前校验 Cron，无效配置不静默注册。</returns>
    private async Task<string?> ResumeOneAsync(string jobId, CancellationToken cancellationToken)
    {
        var config = await repository.FindAsync(jobId, cancellationToken);
        if (config is null)
        {
            return $"{jobId} 不存在";
        }

        CronExpressionGuard.EnsureValid(config.CronExpr);
        var resumed = await repository.UpdateAsync(config, row => row.IsEnabled = true, cancellationToken);
        scheduler.AddOrUpdateRecurring(resumed!.JobId, resumed.JobType, resumed.CronExpr, resumed.Queue);
        return null;
    }

    /// <returns>null 表示成功，否则为错误描述。</returns>
    private async Task<string?> TriggerOneAsync(string jobId, CancellationToken cancellationToken)
    {
        var config = await repository.FindAsync(jobId, cancellationToken);
        if (config is null)
        {
            return $"{jobId} 不存在";
        }

        if (!config.IsEnabled)
        {
            return $"{jobId} 已暂停";
        }

        scheduler.Enqueue(config.JobId, config.JobType, config.EffectiveQueue);
        return null;
    }

    public sealed record RunIndexViewModel(
        IReadOnlyList<TaskConfigView> Tasks,
        string Query,
        string Queue,
        IReadOnlyList<string> Queues,
        bool ReadOnly);
}
