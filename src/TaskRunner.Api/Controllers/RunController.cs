using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TaskRunner.Api.Auth;
using TaskRunner.Api.Services;
using TaskRunner.Core.Common;
using TaskRunner.Core.Models;
using TaskRunner.Core.Services;

namespace TaskRunner.Api.Controllers;

/// <summary>任务运行台（MVC）：服务端过滤与触发，需管理员会话。</summary>
[Authorize(Policy = AdminAuthDefaults.Policy)]
[Route("run")]
public sealed class RunController(
    ITaskConfigRepository repository,
    ITaskScheduler scheduler,
    HangfireMonitoringReader monitoring,
    IOptions<TaskRunnerOptions> options) : Controller
{
    [HttpGet("/run")]
    public async Task<IActionResult> Index(string? q, CancellationToken cancellationToken)
    {
        ViewData["Title"] = "任务运行台 — TaskRunner";
        ViewData["NavActive"] = "run";

        var configs = await repository.ListAsync(cancellationToken);
        var tasks = monitoring.ReadTasks(configs);

        var query = q?.Trim();
        if (!string.IsNullOrEmpty(query))
        {
            tasks = tasks
                .Where(t => t.JobId.Contains(query, StringComparison.OrdinalIgnoreCase)
                            || (t.JobName ?? "").Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return View(new RunIndexViewModel(tasks, query ?? "", options.Value.ReadOnlyMode));
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

    /// <summary>3.5.0 及更早的静态页书签重定向。</summary>
    [HttpGet("/run.html")]
    public IActionResult LegacyRun() => RedirectPermanent("/run");

    public sealed record RunIndexViewModel(
        IReadOnlyList<TaskConfigView> Tasks,
        string Query,
        bool ReadOnly);
}
